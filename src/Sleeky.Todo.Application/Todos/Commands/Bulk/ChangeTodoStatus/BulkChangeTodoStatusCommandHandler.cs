using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.Todos.Recurrence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Events;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.ChangeTodoStatus;

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

        if (request.Status is TodoStatus.Completed or TodoStatus.InProgress)
        {
            await EnsureDependenciesAllowTransitionAsync(
                todos,
                request.Status,
                cancellationToken);
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

        BulkTodoResultItem[] items = todos
            .Select(todoItem =>
            {
                Guid? nextOccurrenceId =
                    nextOccurrenceIds.TryGetValue(todoItem.Id, out Guid occurrenceId)
                        ? occurrenceId
                        : null;
                long version = writtenIds.Contains(todoItem.Id)
                    ? todoItem.Version + 1
                    : todoItem.Version;

                return new BulkTodoResultItem(
                    todoItem.Id,
                    version,
                    todoItem.Status,
                    todoItem.DeletedAt,
                    nextOccurrenceId);
            })
            .ToArray();

        return new BulkTodoResult(items);
    }

    /// <summary>
    /// A dependency is satisfied only when it is completed. Completing the batch
    /// completes every dependency inside it, so membership is itself a guarantee
    /// and a prerequisite may be completed alongside its dependent. Any other
    /// target leaves the selection short of <see cref="TodoStatus.Completed"/>,
    /// so a dependency inside the batch blocks the transition instead of
    /// satisfying it.
    /// </summary>
    private async Task EnsureDependenciesAllowTransitionAsync(
        IReadOnlyList<TodoItem> todos,
        TodoStatus status,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> selectedIds = todos.Select(todoItem => todoItem.Id).ToHashSet();
        Guid[] dependencyIds = todos
            .Where(todoItem => todoItem.Status != status)
            .SelectMany(todoItem => todoItem.DependencyIds)
            .Distinct()
            .ToArray();
        Guid[] externalDependencyIds = dependencyIds
            .Where(dependencyId => !selectedIds.Contains(dependencyId))
            .ToArray();

        if (status != TodoStatus.Completed
            && externalDependencyIds.Length != dependencyIds.Length)
        {
            throw new DomainException($"A blocked TODO cannot move to {status}.");
        }

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
            throw new DomainException($"A blocked TODO cannot move to {status}.");
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
