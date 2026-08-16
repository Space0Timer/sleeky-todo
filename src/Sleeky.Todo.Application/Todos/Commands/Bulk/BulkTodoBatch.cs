using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk;

/// <summary>
/// The mechanics every bulk command shares: how a selection is loaded and how
/// its writes reach the store. Each handler decides what changes in between.
/// </summary>
internal static class BulkTodoBatch
{
    /// <summary>
    /// Loads every selected TODO in one query, in request order, rejecting the
    /// whole batch when an identifier is missing or its version has moved on.
    /// Ordering matches the single-item handlers: a missing TODO is a 404 before
    /// any version is compared. Restoration is the one batch that selects
    /// deleted TODOs, so it asks for them explicitly.
    /// </summary>
    public static async Task<IReadOnlyList<TodoItem>> LoadAsync(
        ITodoRepository todoRepository,
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken,
        bool includeDeleted = false)
    {
        IReadOnlyCollection<TodoItem> loaded = await todoRepository.GetByIdsAsync(
            items.Select(item => item.Id).ToArray(),
            includeDeleted,
            cancellationToken);
        Dictionary<Guid, TodoItem> todosById = loaded.ToDictionary(todo => todo.Id);

        List<TodoItem> ordered = OrderAsRequested(items, todosById);
        EnsureNoneStale(items, todosById);

        return ordered;
    }

    /// <summary>
    /// Persists a batch's updates and inserts as one unit. A batch that writes
    /// nothing makes no round trip, and one that writes a single document skips
    /// the transaction, which keeps a one-item bulk request working against a
    /// standalone MongoDB deployment. Anything larger commits or rolls back
    /// together, so a conflict on any member leaves every member untouched.
    /// </summary>
    /// <param name="expectDeleted">
    /// Set by a restoring batch, whose stored documents are soft-deleted and
    /// would otherwise match no filter and be reported as a conflict.
    /// </param>
    public static Task SaveAsync(
        ITodoRepository todoRepository,
        ITransactionExecutor transactionExecutor,
        IReadOnlyCollection<TodoItem> updates,
        IReadOnlyCollection<TodoItem> inserts,
        CancellationToken cancellationToken,
        bool expectDeleted = false)
    {
        int writeCount = updates.Count + inserts.Count;
        if (writeCount == 0)
        {
            return Task.CompletedTask;
        }

        if (writeCount == 1)
        {
            return todoRepository.SaveBatchAsync(
                updates,
                inserts,
                cancellationToken,
                expectDeleted);
        }

        return transactionExecutor.ExecuteAsync(
            async transactionCancellationToken =>
            {
                await todoRepository.SaveBatchAsync(
                    updates,
                    inserts,
                    transactionCancellationToken,
                    expectDeleted);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Returns the loaded TODOs in the order they were asked for, so a result
    /// item lines up with the request item at the same position. The first
    /// identifier that did not load fails the batch here.
    /// </summary>
    private static List<TodoItem> OrderAsRequested(
        IReadOnlyCollection<BulkTodoItemRequest> items,
        IReadOnlyDictionary<Guid, TodoItem> todosById)
    {
        List<TodoItem> ordered = new List<TodoItem>(items.Count);

        foreach (BulkTodoItemRequest item in items)
        {
            if (!todosById.TryGetValue(item.Id, out TodoItem? todoItem))
            {
                throw new NotFoundException("TODO", item.Id);
            }

            ordered.Add(todoItem);
        }

        return ordered;
    }

    /// <summary>
    /// Every stale identifier is reported together, so a client can refresh
    /// exactly the items that moved rather than discovering them one retry at
    /// a time.
    /// </summary>
    private static void EnsureNoneStale(
        IReadOnlyCollection<BulkTodoItemRequest> items,
        IReadOnlyDictionary<Guid, TodoItem> todosById)
    {
        Guid[] staleIds = items
            .Where(item => todosById[item.Id].Version != item.Version)
            .Select(item => item.Id)
            .ToArray();

        if (staleIds.Length > 0)
        {
            throw new BulkConcurrencyConflictException("TODO", staleIds);
        }
    }
}
