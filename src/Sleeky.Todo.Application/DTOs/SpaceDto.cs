using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.DTOs;

/// <summary>
/// A Space in full, as returned to a member.
/// </summary>
/// <param name="Permission">The current user's own level in the Space.</param>
/// <param name="Version">
/// The token a caller sends back with a rename or a membership change.
/// </param>
public sealed record SpaceDto(
    Guid Id,
    string Name,
    IReadOnlyCollection<SpaceAccessDto> Access,
    SpacePermission Permission,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
