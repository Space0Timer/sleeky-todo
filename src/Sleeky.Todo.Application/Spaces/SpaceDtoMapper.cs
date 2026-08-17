using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Spaces;

/// <summary>
/// Shapes a <see cref="Space"/> for the member reading it.
/// </summary>
/// <remarks>
/// Every Space command and the single-Space query answer with the same
/// <see cref="SpaceDto"/>, so the two things that shape depends on the reader
/// — whose level <see cref="SpaceDto.Permission"/> reports, and the display
/// names the directory holds for the access list — are resolved here once
/// rather than in each handler.
/// </remarks>
internal static class SpaceDtoMapper
{
    /// <summary>
    /// The full view of a Space, with each access entry carrying the display
    /// name the user directory holds for its subject.
    /// </summary>
    public static async Task<SpaceDto> ToDtoAsync(
        Space space,
        Guid currentUserId,
        IUserDirectoryRepository users,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string?> displayNames = await LoadDisplayNamesAsync(
            space,
            users,
            cancellationToken);

        return new SpaceDto(
            space.Id,
            space.Name,
            space.Access.Select(entry => ToAccessDto(entry, displayNames)).ToArray(),
            RequirePermissionOf(space, currentUserId),
            space.Version,
            space.CreatedAt,
            space.UpdatedAt);
    }

    public static SpaceSummaryDto ToSummaryDto(Space space, Guid currentUserId)
    {
        return new SpaceSummaryDto(
            space.Id,
            space.Name,
            RequirePermissionOf(space, currentUserId));
    }

    /// <summary>
    /// The reader's own level. Every caller reaches here as a member — the
    /// access behavior, the creator's Owner grant, or a membership listing put
    /// them there — so absence is a broken invariant, not an answer.
    /// </summary>
    private static SpacePermission RequirePermissionOf(Space space, Guid userId)
    {
        return space.PermissionFor(userId, SubjectType.User)
            ?? throw new InvalidOperationException(
                $"User '{userId}' has no access to Space '{space.Id}'.");
    }

    private static SpaceAccessDto ToAccessDto(
        SpaceAccessEntry entry,
        IReadOnlyDictionary<Guid, string?> displayNames)
    {
        displayNames.TryGetValue(entry.SubjectId, out string? displayName);

        return new SpaceAccessDto(
            entry.SubjectId,
            entry.SubjectType,
            entry.Permission,
            displayName);
    }

    /// <summary>
    /// One directory read for the whole access list. A subject the directory
    /// does not know is simply absent from the result and shows no name.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, string?>> LoadDisplayNamesAsync(
        Space space,
        IUserDirectoryRepository users,
        CancellationToken cancellationToken)
    {
        Guid[] subjectIds = space.Access
            .Where(entry => entry.SubjectType == SubjectType.User)
            .Select(entry => entry.SubjectId)
            .ToArray();
        IReadOnlyCollection<UserIdentity> identities = await users.FindByIdsAsync(
            subjectIds,
            cancellationToken);

        return identities.ToDictionary(
            identity => identity.UserId,
            identity => identity.DisplayName);
    }
}
