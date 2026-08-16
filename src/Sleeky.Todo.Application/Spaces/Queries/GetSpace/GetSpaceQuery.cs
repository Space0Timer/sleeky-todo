using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Queries.GetSpace;

/// <summary>
/// One Space in full, including its access list. Any member may read it:
/// members can see who else is in the Space and at what level.
/// </summary>
public sealed record GetSpaceQuery(Guid SpaceId) : IRequest<SpaceDto>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Read;
}
