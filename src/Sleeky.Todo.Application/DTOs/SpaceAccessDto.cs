using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.DTOs;

/// <summary>
/// One member of a Space as shown to other members.
/// </summary>
/// <param name="DisplayName">
/// Resolved from the user directory at read time; null when the directory
/// holds no name for the subject.
/// </param>
public sealed record SpaceAccessDto(
    Guid SubjectId,
    SubjectType SubjectType,
    SpacePermission Permission,
    string? DisplayName);
