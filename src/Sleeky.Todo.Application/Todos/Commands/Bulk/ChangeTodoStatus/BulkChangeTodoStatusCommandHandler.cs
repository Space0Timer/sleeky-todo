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

/// <summary>
/// Moves a selection of TODOs to one status as a unit: either every member
/// moves or none does. A member already at the target is left alone — no
/// write, no version bump — and does not gate the rest.
/// </summary>
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
        IReadOnlyList<TodoItem> todos = await BulkTodoBatch.LoadAsync(
            todoRepository,
            request.Items,
            cancellationToken);
        await EnsureDependenciesAllowTransitionAsync(
            todos,
            request.Status,
            cancellationToken);

        List<TodoItem> updates = ApplyStatusChange(todos, request.Status);
        List<TodoItem> inserts = await CreateMissingRecurringSuccessorsAsync(
            updates,
            cancellationToken);
        await BulkTodoBatch.SaveAsync(
            todoRepository,
            transactionExecutor,
            updates,
            inserts,
            cancellationToken);

        this.logger.LogInformation(
            1110,
            "Bulk TODO status change to {Status} changed {TodoCount} TODOs and created {OccurrenceCount} recurring occurrences",
            request.Status,
            updates.Count,
            inserts.Count);

        return BulkTodoResult.FromEntities(todos, written: updates, inserted: inserts);
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

    /// <summary>
    /// The position the successor of <paramref name="completion"/> would take.
    /// A completion without series context is answered with an empty
    /// position, which no stored occurrence matches, so the factory is left to
    /// refuse it with the reason.
    /// </summary>
    private static TodoSeriesOccurrence NextOccurrenceOf(TodoCompletion completion)
    {
        return new TodoSeriesOccurrence(
            completion.SeriesId ?? Guid.Empty,
            (completion.OccurrenceNumber ?? 0) + 1);
    }

    /// <summary>
    /// Only moves toward doing the work are gated by prerequisites. Archiving,
    /// unarchiving, and reopening are bookkeeping and ignore them, as the
    /// single-item command does.
    /// </summary>
    /// <remarks>
    /// A dependency is satisfied only when it is completed. Completing the batch
    /// completes every dependency inside it, so membership is itself a guarantee
    /// and a prerequisite may be completed alongside its dependent. Any other
    /// target leaves the selection short of <see cref="TodoStatus.Completed"/>,
    /// so a dependency inside the batch blocks the transition instead of
    /// satisfying it — and it blocks without a read, because the batch's own
    /// move is what leaves it incomplete, whatever the store currently says.
    /// </remarks>
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
    /// One successor per recurring completion whose next occurrence does not
    /// exist yet; every other update inserts nothing.
    /// </summary>
    /// <remarks>
    /// A reopened occurrence completed again already has its successor from
    /// the first time round, so it is completed without one rather than
    /// colliding with the unique series index — the same rule the single-item
    /// command holds. One read answers it for the whole batch.
    /// </remarks>
    private async Task<List<TodoItem>> CreateMissingRecurringSuccessorsAsync(
        IReadOnlyCollection<TodoItem> updates,
        CancellationToken cancellationToken)
    {
        List<TodoCompletion> recurringCompletions = updates
            .Select(todoItem => todoItem.Completion)
            .OfType<TodoCompletion>()
            .Where(completion => completion.Recurrence is not null)
            .ToList();
        if (recurringCompletions.Count == 0)
        {
            return [];
        }

        HashSet<TodoSeriesOccurrence> existing = await LoadExistingNextOccurrencesAsync(
            recurringCompletions,
            cancellationToken);

        return recurringCompletions
            .Where(completion => !existing.Contains(NextOccurrenceOf(completion)))
            .Select(recurringOccurrenceFactory.CreateNext)
            .ToList();
    }

    private async Task<HashSet<TodoSeriesOccurrence>> LoadExistingNextOccurrencesAsync(
        IReadOnlyCollection<TodoCompletion> recurringCompletions,
        CancellationToken cancellationToken)
    {
        TodoSeriesOccurrence[] nextOccurrences = recurringCompletions
            .Where(completion => completion.SeriesId is not null
                && completion.OccurrenceNumber is not null)
            .Select(NextOccurrenceOf)
            .Distinct()
            .ToArray();
        IReadOnlyCollection<TodoSeriesOccurrence> existing =
            await todoRepository.GetExistingSeriesOccurrencesAsync(
                nextOccurrences,
                cancellationToken);

        return existing.ToHashSet();
    }
}
