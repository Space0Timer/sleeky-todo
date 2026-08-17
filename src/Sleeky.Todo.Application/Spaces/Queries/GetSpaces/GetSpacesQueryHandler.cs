using MediatR;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Queries.GetSpaces;

public sealed class GetSpacesQueryHandler
    : IRequestHandler<GetSpacesQuery, IReadOnlyList<SpaceSummaryDto>>
{
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly ISpaceRepository spaces;

    public GetSpacesQueryHandler(
        ISpaceRepository spaces,
        IClock clock,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(spaces);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(currentUser);

        this.spaces = spaces;
        this.clock = clock;
        this.currentUser = currentUser;
    }

    public async Task<IReadOnlyList<SpaceSummaryDto>> Handle(
        GetSpacesQuery request,
        CancellationToken cancellationToken)
    {
        Guid userId = currentUser.UserId;
        await EnsurePersonalSpaceAsync(userId, cancellationToken);

        IReadOnlyCollection<Space> memberships = await spaces.GetForSubjectAsync(
            userId,
            SubjectType.User,
            cancellationToken);

        return memberships
            .Select(space => SpaceDtoMapper.ToSummaryDto(space, userId))
            .ToArray();
    }

    /// <summary>
    /// Creates the personal Space on the user's first listing and is a no-op
    /// on every later one. The identifier is derived from the user, so the
    /// insert is idempotent and two first requests racing here both find the
    /// same Space rather than one of them failing.
    /// </summary>
    private async Task EnsurePersonalSpaceAsync(Guid userId, CancellationToken cancellationToken)
    {
        Space personalSpace = Space.Create(
            PersonalSpace.IdFor(userId),
            PersonalSpace.Name,
            userId,
            clock.UtcNow);

        _ = await spaces.GetOrAddAsync(personalSpace, cancellationToken);
    }
}
