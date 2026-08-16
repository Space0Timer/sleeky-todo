using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.DTOs;

/// <summary>
/// One row of a user's Space list: enough to pick a Space and to know what
/// the picker may do there, nothing more.
/// </summary>
/// <param name="Permission">The current user's level in the Space.</param>
public sealed record SpaceSummaryDto(
    Guid Id,
    string Name,
    SpacePermission Permission);
