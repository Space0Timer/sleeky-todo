using MediatR;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Spaces.Queries.GetSpace;

public sealed class GetSpaceQueryHandler : IRequestHandler<GetSpaceQuery, SpaceDto>
{
    private const string ResourceName = "Space";

    private readonly ICurrentUser currentUser;
    private readonly ISpaceRepository spaces;
    private readonly IUserDirectoryRepository users;

    public GetSpaceQueryHandler(
        ISpaceRepository spaces,
        IUserDirectoryRepository users,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(spaces);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(currentUser);

        this.spaces = spaces;
        this.users = users;
        this.currentUser = currentUser;
    }

    public async Task<SpaceDto> Handle(
        GetSpaceQuery request,
        CancellationToken cancellationToken)
    {
        Space space = await spaces.GetByIdAsync(request.SpaceId, cancellationToken)
            ?? throw new NotFoundException(ResourceName, request.SpaceId);

        return await SpaceDtoMapper.ToDtoAsync(
            space,
            currentUser.UserId,
            users,
            cancellationToken);
    }
}
