using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Spaces.Commands.RenameSpace;

public sealed class RenameSpaceCommandHandler : IRequestHandler<RenameSpaceCommand, SpaceDto>
{
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly ILogger<RenameSpaceCommandHandler> logger;
    private readonly ISpaceRepository spaces;
    private readonly IUserDirectoryRepository users;

    public RenameSpaceCommandHandler(
        ISpaceRepository spaces,
        IUserDirectoryRepository users,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<RenameSpaceCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(spaces);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(logger);

        this.spaces = spaces;
        this.users = users;
        this.clock = clock;
        this.currentUser = currentUser;
        this.logger = logger;
    }

    public async Task<SpaceDto> Handle(
        RenameSpaceCommand request,
        CancellationToken cancellationToken)
    {
        Space space = await spaces.GetRequiredAsync(
            request.SpaceId,
            request.Version,
            cancellationToken);

        space.Rename(request.Name, clock.UtcNow);
        Space renamed = await spaces.UpdateAsync(space, cancellationToken);

        this.logger.LogInformation(
            1122,
            "Renamed Space {SpaceId} from version {PreviousVersion} to {Version}",
            renamed.Id,
            request.Version,
            renamed.Version);

        return await SpaceDtoMapper.ToDtoAsync(
            renamed,
            currentUser.UserId,
            users,
            cancellationToken);
    }
}
