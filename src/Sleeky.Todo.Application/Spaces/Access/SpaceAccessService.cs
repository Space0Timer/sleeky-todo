using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Services;

namespace Sleeky.Todo.Application.Spaces.Access;

public sealed class SpaceAccessService : ISpaceAccessService
{
    private const string ResourceName = "Space";

    private readonly ICurrentUser currentUser;
    private readonly SpaceScope scope;
    private readonly ISpaceRepository spaces;

    public SpaceAccessService(
        ISpaceRepository spaces,
        ICurrentUser currentUser,
        SpaceScope scope)
    {
        ArgumentNullException.ThrowIfNull(spaces);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(scope);

        this.spaces = spaces;
        this.currentUser = currentUser;
        this.scope = scope;
    }

    public async Task<SpaceAccessContext> RequireAsync(
        Guid spaceId,
        SpacePermission requiredPermission,
        CancellationToken cancellationToken = default)
    {
        (Space space, SpacePermission granted) = await LoadMembershipAsync(spaceId, cancellationToken);
        EnsureLevel(space, granted, requiredPermission);

        SpaceAccessContext context = new SpaceAccessContext(space.Id, space.Name, granted);
        scope.Bind(context);

        return context;
    }

    private static void EnsureLevel(
        Space space,
        SpacePermission granted,
        SpacePermission requiredPermission)
    {
        if (!SpacePermissions.Includes(granted, requiredPermission))
        {
            throw new ForbiddenException(ResourceName, space.Id, requiredPermission.ToString());
        }
    }

    /// <summary>
    /// A missing Space and a Space the user is not a member of are the same
    /// answer, so a probe cannot tell an unknown identifier from someone
    /// else's Space.
    /// </summary>
    private async Task<(Space Space, SpacePermission Granted)> LoadMembershipAsync(
        Guid spaceId,
        CancellationToken cancellationToken)
    {
        Space? space = await spaces.GetByIdAsync(spaceId, cancellationToken);
        SpacePermission? granted = space?.PermissionFor(currentUser.UserId, SubjectType.User);

        if (space is null || granted is null)
        {
            throw new NotFoundException(ResourceName, spaceId);
        }

        return (space, granted.Value);
    }
}
