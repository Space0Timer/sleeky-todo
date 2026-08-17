using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Commands.RemoveSpaceAccess;

/// <summary>
/// Revokes a member's access to a Space. Owner-only.
/// </summary>
/// <param name="Version">
/// The Space version the caller last saw; a Space that has moved on since
/// is a concurrency conflict.
/// </param>
public sealed record RemoveSpaceAccessCommand(
    Guid SpaceId,
    Guid SubjectId,
    long Version) : IRequest<SpaceDto>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Owner;
}
