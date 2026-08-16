using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Spaces.Commands.RemoveSpaceAccess;

public sealed class RemoveSpaceAccessCommandHandler
    : IRequestHandler<RemoveSpaceAccessCommand, SpaceDto>
{
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly ILogger<RemoveSpaceAccessCommandHandler> logger;
    private readonly ISpaceRepository spaces;
    private readonly IUserDirectoryRepository users;

    public RemoveSpaceAccessCommandHandler(
        ISpaceRepository spaces,
        IUserDirectoryRepository users,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<RemoveSpaceAccessCommandHandler> logger)
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
        RemoveSpaceAccessCommand request,
        CancellationToken cancellationToken)
    {
        EnsureNotSelf(request.SubjectId);
        Space space = await spaces.GetRequiredAsync(
            request.SpaceId,
            request.Version,
            cancellationToken);

        // The entity refuses to remove the last Owner, so a Space is never left
        // without one.
        space.RemoveAccess(request.SubjectId, SubjectType.User, clock.UtcNow);
        Space updated = await spaces.UpdateAsync(space, cancellationToken);

        this.logger.LogInformation(
            1125,
            "Revoked user {SubjectId} access to Space {SpaceId} at version {Version}",
            request.SubjectId,
            updated.Id,
            updated.Version);

        return await SpaceDtoMapper.ToDtoAsync(
            updated,
            currentUser.UserId,
            users,
            cancellationToken);
    }

    /// <summary>
    /// Revoking one's own access is leaving the Space, which is not an
    /// operation this command offers: the caller would be answered with a
    /// Space they no longer belong to. Refused as a rule of the operation, the
    /// same way the entity refuses to remove the last Owner.
    /// </summary>
    private void EnsureNotSelf(Guid subjectId)
    {
        if (subjectId == currentUser.UserId)
        {
            throw new DomainException("A member cannot revoke their own access to a Space.");
        }
    }
}
