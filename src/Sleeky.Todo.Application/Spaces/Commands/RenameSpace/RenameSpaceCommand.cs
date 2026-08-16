using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Commands.RenameSpace;

/// <summary>
/// Renames a Space. Owner-only, like every change to the Space itself.
/// </summary>
/// <param name="Version">
/// The Space version the caller last saw; a Space that has moved on since
/// is a concurrency conflict.
/// </param>
public sealed record RenameSpaceCommand(
    Guid SpaceId,
    string Name,
    long Version) : IRequest<SpaceDto>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Owner;
}
