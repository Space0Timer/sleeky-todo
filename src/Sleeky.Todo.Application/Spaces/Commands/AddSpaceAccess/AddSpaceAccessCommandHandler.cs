using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Commands.AddSpaceAccess;

public sealed class AddSpaceAccessCommandHandler
    : IRequestHandler<AddSpaceAccessCommand, SpaceDto>
{
    private const string UserResourceName = "User";

    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly ILogger<AddSpaceAccessCommandHandler> logger;
    private readonly ISpaceRepository spaces;
    private readonly IUserDirectoryRepository users;

    public AddSpaceAccessCommandHandler(
        ISpaceRepository spaces,
        IUserDirectoryRepository users,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<AddSpaceAccessCommandHandler> logger)
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
        AddSpaceAccessCommand request,
        CancellationToken cancellationToken)
    {
        Space space = await spaces.GetRequiredAsync(
            request.SpaceId,
            request.Version,
            cancellationToken);
        await EnsureUserExistsAsync(request.SubjectId, cancellationToken);

        space.AddAccess(
            request.SubjectId,
            SubjectType.User,
            request.Permission,
            clock.UtcNow);
        Space updated = await spaces.UpdateAsync(space, cancellationToken);

        this.logger.LogInformation(
            1123,
            "Granted user {SubjectId} {Permission} access to Space {SpaceId} at version {Version}",
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

    /// <summary>
    /// Only a user the directory knows — one who has signed in at least once —
    /// can be granted access. The Space entity cannot tell a real user from a
    /// made-up identifier, so the directory is asked here before the grant is
    /// recorded.
    /// </summary>
    private async Task EnsureUserExistsAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<UserIdentity> identities = await users.FindByIdsAsync(
            [subjectId],
            cancellationToken);

        if (identities.Count == 0)
        {
            throw new NotFoundException(UserResourceName, subjectId);
        }
    }
}
