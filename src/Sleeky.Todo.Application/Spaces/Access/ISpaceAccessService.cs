using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Access;

/// <summary>
/// Decides whether the current user may act in a Space at a given level, and
/// binds the request's <see cref="Abstractions.Identity.ISpaceScope"/> when
/// they may.
/// </summary>
/// <remarks>
/// One implementation serves both callers: the pipeline behavior that guards
/// every scoped command and query, and the assistant, which has to refuse a
/// turn before any model is called rather than at the first tool.
/// </remarks>
public interface ISpaceAccessService
{
    /// <summary>
    /// Requires the current user to hold at least
    /// <paramref name="requiredPermission"/> in the Space.
    /// </summary>
    /// <returns>What was established, for callers that need the name or level.</returns>
    /// <exception cref="Exceptions.NotFoundException">
    /// The Space does not exist, or the user has no access to it. The two are
    /// deliberately the same answer.
    /// </exception>
    /// <exception cref="Exceptions.ForbiddenException">
    /// The user has access, but below the required level.
    /// </exception>
    Task<SpaceAccessContext> RequireAsync(
        Guid spaceId,
        SpacePermission requiredPermission,
        CancellationToken cancellationToken = default);
}
