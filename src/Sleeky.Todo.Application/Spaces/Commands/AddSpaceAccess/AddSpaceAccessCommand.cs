using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Commands.AddSpaceAccess;

/// <summary>
/// Grants a user access to a Space at the given level. Owner-only.
/// </summary>
/// <remarks>
/// The subject is always a user: sharing with anything else is not offered
/// through the API, so the command carries no subject type.
/// </remarks>
/// <param name="Version">
/// The Space version the caller last saw; a Space that has moved on since
/// is a concurrency conflict.
/// </param>
public sealed record AddSpaceAccessCommand(
    Guid SpaceId,
    Guid SubjectId,
    SpacePermission Permission,
    long Version) : IRequest<SpaceDto>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Owner;
}
