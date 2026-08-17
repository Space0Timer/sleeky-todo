using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos;

internal static class TodoRepositoryExtensions
{
    /// <summary>
    /// Loads a TODO by identifier, or answers 404 the way every route that
    /// names one does.
    /// </summary>
    /// <remarks>
    /// The read counterpart of the versioned overload, and the reason both
    /// exist as one helper: a query names a TODO without claiming to know its
    /// version, so it needs the not-found half of that promise and nothing
    /// else. Written once here, so a handler cannot phrase the same 404
    /// slightly differently.
    /// </remarks>
    /// <param name="includeDeleted">
    /// Set when the target is deleted by definition and would otherwise be
    /// hidden by the soft-delete filter.
    /// </param>
    public static async Task<TodoItem> GetRequiredAsync(
        this ITodoRepository todoRepository,
        Guid id,
        CancellationToken cancellationToken,
        bool includeDeleted = false)
    {
        return await todoRepository.GetByIdAsync(id, includeDeleted, cancellationToken)
            ?? throw new NotFoundException("TODO", id);
    }

    /// <summary>
    /// Loads the TODO a client says it holds, or fails the way every single-item
    /// command promises: a missing identifier is a 404 before any version is
    /// compared, and a version that has moved on is a 409. The single-item
    /// counterpart of <c>BulkTodoBatch.LoadAsync</c>.
    /// </summary>
    /// <param name="includeDeleted">
    /// Set by restoration, the one command whose target is deleted by
    /// definition and would otherwise be hidden by the soft-delete filter.
    /// </param>
    public static async Task<TodoItem> GetRequiredAsync(
        this ITodoRepository todoRepository,
        Guid id,
        long expectedVersion,
        CancellationToken cancellationToken,
        bool includeDeleted = false)
    {
        TodoItem todoItem = await todoRepository.GetRequiredAsync(
            id,
            cancellationToken,
            includeDeleted);

        if (todoItem.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException("TODO", todoItem.Id, expectedVersion);
        }

        return todoItem;
    }
}
