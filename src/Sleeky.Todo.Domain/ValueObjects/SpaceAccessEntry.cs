using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Domain.ValueObjects;

/// <summary>
/// One line of a Space's access list: who, and what they may do.
/// </summary>
/// <remarks>
/// A subject is identified by the pair (<see cref="SubjectId"/>,
/// <see cref="SubjectType"/>); the permission is the value that pair carries.
/// The values are validated by <see cref="Entities.Space"/> when an entry is
/// added, so this record itself stays a plain carrier.
/// </remarks>
public sealed record SpaceAccessEntry(
    Guid SubjectId,
    SubjectType SubjectType,
    SpacePermission Permission);
