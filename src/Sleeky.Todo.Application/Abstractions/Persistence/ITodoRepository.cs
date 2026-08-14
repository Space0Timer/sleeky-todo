using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Abstractions.Persistence;

public interface ITodoRepository
{
    Task AddAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default);

    Task<TodoItem?> GetByIdAsync(
        Guid id,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid id,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<TodoItem>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveDependentsAsync(
        Guid dependencyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the aggregate, expecting the version it was loaded at. Throws
    /// <see cref="ConcurrencyConflictException"/> when the stored version has
    /// moved on.
    /// </summary>
    Task<TodoItem> UpdateAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default);

    Task<TodoItem> SoftDeleteAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default);

    Task<TodoItem> RestoreAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default);
}
