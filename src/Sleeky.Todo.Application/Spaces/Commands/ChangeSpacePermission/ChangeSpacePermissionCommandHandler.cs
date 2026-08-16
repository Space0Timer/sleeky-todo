using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Commands.ChangeSpacePermission;

public sealed class ChangeSpacePermissionCommandHandler
    : IRequestHandler<ChangeSpacePermissionCommand, SpaceDto>
{
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly ILogger<ChangeSpacePermissionCommandHandler> logger;
    private readonly ISpaceRepository spaces;
    private readonly IUserDirectoryRepository users;

    public ChangeSpacePermissionCommandHandler(
        ISpaceRepository spaces,
        IUserDirectoryRepository users,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<ChangeSpacePermissionCommandHandler> logger)
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
        ChangeSpacePermissionCommand request,
        CancellationToken cancellationToken)
    {
        Space space = await spaces.GetRequiredAsync(
            request.SpaceId,
            request.Version,
            cancellationToken);

        // The entity refuses to downgrade the last Owner, so an Owner lowering
        // their own level is allowed only while another Owner remains.
        space.ChangePermission(
            request.SubjectId,
            SubjectType.User,
            request.Permission,
            clock.UtcNow);
        Space updated = await spaces.UpdateAsync(space, cancellationToken);

        this.logger.LogInformation(
            1124,
            "Changed user {SubjectId} to {Permission} access in Space {SpaceId} at version {Version}",
            request.SubjectId,
            request.Permission,
            updated.Id,
            updated.Version);

        return await SpaceDtoMapper.ToDtoAsync(
            updated,
            currentUser.UserId,
            users,
            cancellationToken);
    }
}
