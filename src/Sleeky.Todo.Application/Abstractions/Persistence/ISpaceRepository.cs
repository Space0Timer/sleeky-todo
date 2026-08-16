using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Abstractions.Persistence;

/// <summary>
/// Persistence for <see cref="Space"/>. Unlike the TODO repository this one is
/// not scoped to the caller: a Space is what the caller is checked against, so
/// the check itself has to read it unscoped.
/// </summary>
public interface ISpaceRepository
{
    Task AddAsync(Space space, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the Space when no document carries its identifier, otherwise
    /// returns the one already stored.
    /// </summary>
    /// <remarks>
    /// The idempotent primitive a personal Space is created through: its
    /// identifier is derived from the user, so two first requests racing to
    /// create it both come away holding the same Space rather than one of them
    /// failing.
    /// </remarks>
    Task<Space> GetOrAddAsync(Space space, CancellationToken cancellationToken = default);

    Task<Space?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every Space in which the subject has an access entry, oldest first.
    /// </summary>
    Task<IReadOnlyCollection<Space>> GetForSubjectAsync(
        Guid subjectId,
        SubjectType subjectType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the stored document while it still carries the version the
    /// Space was loaded at, and returns the Space as stored — one version on.
    /// </summary>
    /// <exception cref="Exceptions.ConcurrencyConflictException">
    /// The stored version has moved since the Space was read.
    /// </exception>
    Task<Space> UpdateAsync(Space space, CancellationToken cancellationToken = default);
}
