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

    /// <summary>
    /// Reads the dependency-relevant fields of the given TODOs without
    /// materialising whole aggregates. Missing identifiers are simply absent
    /// from the result, exactly as with <see cref="GetByIdsAsync"/>.
    /// </summary>
    Task<IReadOnlyCollection<TodoDependencyNode>> GetDependencyNodesAsync(
        IEnumerable<Guid> ids,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveDependentsAsync(
        Guid dependencyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the identifiers of active, non-archived TODOs that depend on any
    /// of <paramref name="dependencyIds"/>, ignoring those in
    /// <paramref name="excludedIds"/>. A batch deletion excludes its own members
    /// so that deleting a prerequisite together with its dependent is allowed.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetActiveDependentIdsAsync(
        IReadOnlyCollection<Guid> dependencyIds,
        IReadOnlyCollection<Guid> excludedIds,
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

    /// <summary>
    /// Persists every aggregate as one batch, replacing each update at the
    /// version it was loaded at and inserting each new aggregate. Throws
    /// <see cref="BulkConcurrencyConflictException"/> when any replacement no
    /// longer matches or any insert collides, leaving the batch unapplied when
    /// it runs inside a transaction.
    /// </summary>
    /// <summary>
    /// Applies every write as one batch. <paramref name="expectDeleted"/> is set
    /// by a restoring batch, whose stored documents are soft-deleted and would
    /// otherwise match no filter and be reported as a conflict.
    /// </summary>
    Task SaveBatchAsync(
        IReadOnlyCollection<TodoItem> updates,
        IReadOnlyCollection<TodoItem> inserts,
        CancellationToken cancellationToken = default,
        bool expectDeleted = false);
}
