using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.Todos.Recurrence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.ValueObjects;

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
        await EnsureDependenciesAllowTransitionAsync(
            todos,
            request.Status,
            cancellationToken);

        List<TodoItem> updates = ApplyStatusChange(todos, request.Status);
        List<TodoItem> inserts = CreateRecurringSuccessors(updates);
        await PersistAsync(updates, inserts, cancellationToken);

        this.logger.LogInformation(
            1110,
            "Bulk TODO status change to {Status} changed {TodoCount} TODOs and created {OccurrenceCount} recurring occurrences",
            request.Status,
            updates.Count,
            inserts.Count);

        return BuildResult(todos, updates);
    }

    /// <summary>
    /// The prerequisites of every item the change would actually move. An item
    /// already at the target status is a no-op and gates nothing.
    /// </summary>
    private static Guid[] CollectDependencyIdsOfPendingItems(
        IReadOnlyList<TodoItem> todos,
        TodoStatus status)
    {
        return todos
            .Where(todoItem => todoItem.Status != status)
            .SelectMany(todoItem => todoItem.DependencyIds)
            .Distinct()
            .ToArray();
    }

    private static DomainException BlockedTransition(TodoStatus status)
    {
        return new DomainException($"A blocked TODO cannot move to {status}.");
    }

    private static BulkTodoResult BuildResult(
        IReadOnlyList<TodoItem> todos,
        IReadOnlyCollection<TodoItem> updates)
    {
        HashSet<Guid> writtenIds = updates.Select(todoItem => todoItem.Id).ToHashSet();

        BulkTodoResultItem[] items = todos
            .Select(todoItem => ToResultItem(todoItem, writtenIds.Contains(todoItem.Id)))
            .ToArray();

        return new BulkTodoResult(items);
    }

    /// <summary>
    /// The version reported is the one the write produced, which the entity
    /// itself never advances; an item that needed no write echoes its own. The
    /// successor's identifier is read from the completion, where the entity
    /// fixed it, rather than tracked separately.
    /// </summary>
    private static BulkTodoResultItem ToResultItem(TodoItem todoItem, bool written)
    {
        return new BulkTodoResultItem(
            todoItem.Id,
            written ? todoItem.Version + 1 : todoItem.Version,
            todoItem.Status,
            todoItem.DeletedAt,
            todoItem.Completion?.NextOccurrenceId);
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
        if (status is not (TodoStatus.Completed or TodoStatus.InProgress))
        {
            return;
        }

        HashSet<Guid> selectedIds = todos.Select(todoItem => todoItem.Id).ToHashSet();
        Guid[] dependencyIds = CollectDependencyIdsOfPendingItems(todos, status);
        Guid[] externalDependencyIds = dependencyIds
            .Where(dependencyId => !selectedIds.Contains(dependencyId))
            .ToArray();

        bool hasDependencyInsideBatch = externalDependencyIds.Length != dependencyIds.Length;
        if (status != TodoStatus.Completed && hasDependencyInsideBatch)
        {
            throw BlockedTransition(status);
        }

        if (externalDependencyIds.Length == 0)
        {
            return;
        }

        if (await AnyDependencyIncompleteAsync(externalDependencyIds, cancellationToken))
        {
            throw BlockedTransition(status);
        }
    }

    /// <summary>
    /// A missing, deleted, or not-yet-completed prerequisite each count as
    /// incomplete, so the read looks past the soft-delete filter to tell the
    /// last two apart from the first.
    /// </summary>
    private async Task<bool> AnyDependencyIncompleteAsync(
        IReadOnlyCollection<Guid> dependencyIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<TodoItem> dependencies = await todoRepository.GetByIdsAsync(
            dependencyIds,
            includeDeleted: true,
            cancellationToken);
        Dictionary<Guid, TodoItem> dependenciesById = dependencies.ToDictionary(
            dependency => dependency.Id);

        return dependencyIds.Any(dependencyId =>
            !dependenciesById.TryGetValue(dependencyId, out TodoItem? dependency)
            || dependency.DeletedAt is not null
            || dependency.Status != TodoStatus.Completed);
    }

    /// <summary>
    /// Moves every item and returns the ones that actually changed. One instant
    /// is read for the whole batch so every write it makes shares it.
    /// </summary>
    private List<TodoItem> ApplyStatusChange(
        IReadOnlyList<TodoItem> todos,
        TodoStatus status)
    {
        DateTimeOffset timestamp = clock.UtcNow;
        List<TodoItem> updates = new List<TodoItem>(todos.Count);

        foreach (TodoItem todoItem in todos)
        {
            if (todoItem.ChangeStatus(status, timestamp))
            {
                updates.Add(todoItem);
            }
        }

        return updates;
    }

    /// <summary>
    /// One successor per recurring completion; every other update inserts
    /// nothing.
    /// </summary>
    private List<TodoItem> CreateRecurringSuccessors(IReadOnlyCollection<TodoItem> updates)
    {
        List<TodoItem> inserts = new List<TodoItem>();

        foreach (TodoItem todoItem in updates)
        {
            TodoCompletion? completion = todoItem.Completion;
            if (completion?.Recurrence is not null)
            {
                inserts.Add(recurringOccurrenceFactory.CreateNext(completion));
            }
        }

        return inserts;
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
