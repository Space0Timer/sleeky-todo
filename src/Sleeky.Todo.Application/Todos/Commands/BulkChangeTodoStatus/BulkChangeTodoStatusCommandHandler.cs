using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Recurrence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Events;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Todos.Commands.BulkChangeTodoStatus;

public sealed class BulkChangeTodoStatusCommandHandler
    : IRequestHandler<BulkChangeTodoStatusCommand, BulkTodoResult>
{
    private readonly IClock clock;
    private readonly ILogger<BulkChangeTodoStatusCommandHandler> logger;
    private readonly IRecurringOccurrenceFactory recurringOccurrenceFactory;
    private readonly ITodoRepository todoRepository;
    private readonly ITransactionExecutor transactionExecutor;

    public BulkChangeTodoStatusCommandHandler(
        ITodoRepository todoRepository,
        IRecurringOccurrenceFactory recurringOccurrenceFactory,
        IClock clock,
        ITransactionExecutor transactionExecutor,
        ILogger<BulkChangeTodoStatusCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(recurringOccurrenceFactory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(transactionExecutor);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.recurringOccurrenceFactory = recurringOccurrenceFactory;
        this.clock = clock;
        this.transactionExecutor = transactionExecutor;
        this.logger = logger;
    }

    public async Task<BulkTodoResult> Handle(
        BulkChangeTodoStatusCommand request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TodoItem> todos = await BulkTodoLoader.LoadAsync(
            todoRepository,
            request.Items,
            cancellationToken);

        if (request.Status == TodoStatus.Completed)
        {
            await EnsureDependenciesAllowCompletionAsync(todos, cancellationToken);
        }

        List<TodoItem> updates = new List<TodoItem>(todos.Count);
        List<TodoItem> inserts = new List<TodoItem>();
        Dictionary<Guid, Guid> nextOccurrenceIds = new Dictionary<Guid, Guid>();
        DateTimeOffset timestamp = clock.UtcNow;

        foreach (TodoItem todoItem in todos)
        {
            if (!todoItem.ChangeStatus(request.Status, timestamp))
            {
                continue;
            }

            updates.Add(todoItem);
            TodoCompletedDomainEvent? completion = todoItem.DomainEvents
                .OfType<TodoCompletedDomainEvent>()
                .SingleOrDefault();
            if (completion?.CompletionContext.Recurrence is not null)
            {
                inserts.Add(recurringOccurrenceFactory.CreateNext(completion));
                nextOccurrenceIds[todoItem.Id] = completion.NextOccurrenceId!.Value;
            }

            todoItem.ClearDomainEvents();
        }

        await PersistAsync(updates, inserts, cancellationToken);

        this.logger.LogInformation(
            1110,
            "Bulk TODO status change to {Status} changed {TodoCount} TODOs and created {OccurrenceCount} recurring occurrences",
            request.Status,
            updates.Count,
            inserts.Count);

        return BuildResult(todos, updates, nextOccurrenceIds);
    }

    private static BulkTodoResult BuildResult(
        IReadOnlyList<TodoItem> todos,
        IReadOnlyCollection<TodoItem> updates,
        IReadOnlyDictionary<Guid, Guid> nextOccurrenceIds)
    {
        HashSet<Guid> writtenIds = updates.Select(todoItem => todoItem.Id).ToHashSet();

        return new BulkTodoResult(todos
            .Select(todoItem => new BulkTodoResultItem(
                todoItem.Id,
                writtenIds.Contains(todoItem.Id) ? todoItem.Version + 1 : todoItem.Version,
                todoItem.Status,
                todoItem.DeletedAt,
                nextOccurrenceIds.TryGetValue(todoItem.Id, out Guid nextOccurrenceId)
                    ? nextOccurrenceId
                    : null))
            .ToArray());
    }

    /// <summary>
    /// A dependency is satisfied when it is already completed or is part of this
    /// batch, which lets a prerequisite and its dependent complete together. Any
    /// selected TODO that cannot reach <see cref="TodoStatus.Completed"/> fails
    /// the batch on its own, so batch membership is a safe guarantee.
    /// </summary>
    private async Task EnsureDependenciesAllowCompletionAsync(
        IReadOnlyList<TodoItem> todos,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> selectedIds = todos.Select(todoItem => todoItem.Id).ToHashSet();
        Guid[] externalDependencyIds = todos
            .Where(todoItem => todoItem.Status != TodoStatus.Completed)
            .SelectMany(todoItem => todoItem.DependencyIds)
            .Where(dependencyId => !selectedIds.Contains(dependencyId))
            .Distinct()
            .ToArray();
        if (externalDependencyIds.Length == 0)
        {
            return;
        }

        IReadOnlyCollection<TodoItem> dependencies = await todoRepository.GetByIdsAsync(
            externalDependencyIds,
            includeDeleted: true,
            cancellationToken);
        Dictionary<Guid, TodoItem> dependenciesById = dependencies.ToDictionary(
            dependency => dependency.Id);
        bool blocked = externalDependencyIds.Any(dependencyId =>
            !dependenciesById.TryGetValue(dependencyId, out TodoItem? dependency)
            || dependency.DeletedAt is not null
            || dependency.Status != TodoStatus.Completed);

        if (blocked)
        {
            throw new DomainException(
                $"A blocked TODO cannot move to {TodoStatus.Completed}.");
        }
    }

    /// <summary>
    /// A batch that writes a single document does not need a transaction, which
    /// keeps bulk requests working against a standalone MongoDB deployment.
    /// </summary>
    private Task PersistAsync(
        IReadOnlyCollection<TodoItem> updates,
        IReadOnlyCollection<TodoItem> inserts,
        CancellationToken cancellationToken)
    {
        if (updates.Count == 0 && inserts.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (updates.Count + inserts.Count == 1)
        {
            return todoRepository.SaveBatchAsync(updates, inserts, cancellationToken);
        }

        return transactionExecutor.ExecuteAsync(
            async transactionCancellationToken =>
            {
                await todoRepository.SaveBatchAsync(
                    updates,
                    inserts,
                    transactionCancellationToken);
                return true;
            },
            cancellationToken);
    }
}
