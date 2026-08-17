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

    /// <remarks>
    /// The access behavior has already established that the caller is a member,
    /// but it did so against its own read of the Space. Between that read and
    /// this one an Owner can remove the caller, and the answer to a read of a
    /// Space the caller no longer belongs to is the answer anyone outside it
    /// gets: not found. The mutating commands need no equivalent, because
    /// removing an access entry moves the Space's version and their version
    /// guard refuses first.
    /// </remarks>
    public async Task<SpaceDto> Handle(
        GetSpaceQuery request,
        CancellationToken cancellationToken)
    {
        Space space = await spaces.GetByIdAsync(request.SpaceId, cancellationToken)
            ?? throw new NotFoundException(ResourceName, request.SpaceId);
        SpaceDto? view = await SpaceDtoMapper.ToDtoIfStillMemberAsync(
            space,
            currentUser.UserId,
            users,
            cancellationToken);

        return view ?? throw new NotFoundException(ResourceName, request.SpaceId);
    }
}
