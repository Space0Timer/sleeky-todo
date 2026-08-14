using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk;

internal static class BulkTodoLoader
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

        List<TodoItem> ordered = new List<TodoItem>(items.Count);
        foreach (BulkTodoItemRequest item in items)
        {
            if (!todosById.TryGetValue(item.Id, out TodoItem? todoItem))
            {
                throw new NotFoundException("TODO", item.Id);
            }

            ordered.Add(todoItem);
        }

        Guid[] staleIds = items
            .Where(item => todosById[item.Id].Version != item.Version)
            .Select(item => item.Id)
            .ToArray();
        if (staleIds.Length > 0)
        {
            throw new BulkConcurrencyConflictException("TODO", staleIds);
        }

        return ordered;
    }
}
