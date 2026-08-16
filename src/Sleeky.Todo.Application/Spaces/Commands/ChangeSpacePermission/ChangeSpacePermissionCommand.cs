using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Commands.ChangeSpacePermission;

/// <summary>
/// Moves an existing member of a Space to a different level. Owner-only.
/// </summary>
/// <param name="Version">
/// The Space version the caller last saw; a Space that has moved on since
/// is a concurrency conflict.
/// </param>
public sealed record ChangeSpacePermissionCommand(
    Guid SpaceId,
    Guid SubjectId,
    SpacePermission Permission,
    long Version) : IRequest<SpaceDto>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Owner;
}
