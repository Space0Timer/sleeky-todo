using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Spaces.Commands.CreateSpace;

public sealed class CreateSpaceCommandHandler : IRequestHandler<CreateSpaceCommand, SpaceDto>
{
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly ILogger<CreateSpaceCommandHandler> logger;
    private readonly ISpaceRepository spaces;
    private readonly IUserDirectoryRepository users;

    public CreateSpaceCommandHandler(
        ISpaceRepository spaces,
        IUserDirectoryRepository users,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<CreateSpaceCommandHandler> logger)
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
        CreateSpaceCommand request,
        CancellationToken cancellationToken)
    {
        Space space = Space.Create(
            Guid.NewGuid(),
            request.Name,
            currentUser.UserId,
            clock.UtcNow);

        await spaces.AddAsync(space, cancellationToken);

        this.logger.LogInformation(
            1121,
            "Created Space {SpaceId} owned by user {UserId}",
            space.Id,
            currentUser.UserId);

        return await SpaceDtoMapper.ToDtoAsync(
            space,
            currentUser.UserId,
            users,
            cancellationToken);
    }
}
