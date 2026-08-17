using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Spaces;

internal static class SpaceRepositoryExtensions
{
    private const string ResourceName = "Space";

    /// <summary>
    /// Loads the Space a client says it holds, or fails the way every Space
    /// mutation promises: a missing identifier is a 404 before any version is
    /// compared, and a version that has moved on is a 409. The Space
    /// counterpart of <c>TodoRepositoryExtensions.GetRequiredAsync</c>.
    /// </summary>
    /// <remarks>
    /// The access behavior has already read the Space once to authorize the
    /// request, so a null here means it vanished in between; that is answered
    /// as not found rather than treated as impossible.
    /// </remarks>
    public static async Task<Space> GetRequiredAsync(
        this ISpaceRepository spaces,
        Guid spaceId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        Space space = await spaces.GetByIdAsync(spaceId, cancellationToken)
            ?? throw new NotFoundException(ResourceName, spaceId);

        if (space.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(ResourceName, space.Id, expectedVersion);
        }

        return space;
    }
}
