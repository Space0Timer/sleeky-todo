using Sleeky.Todo.Application.Abstractions.Identity;

namespace Sleeky.Todo.Application.Abstractions.Persistence;

public interface IUserDirectoryRepository
{
    /// <summary>
    /// Maps an external issuer and subject pair onto a stable internal user
    /// identifier, creating the mapping on first login.
    /// </summary>
    /// <param name="email">
    /// The address the provider reported, or null when it reported none. It is
    /// what lets someone find this user by typing an address rather than a
    /// name.
    /// </param>
    Task<UserIdentity> ResolveAsync(
        string issuer,
        string subject,
        string? displayName,
        string? email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The identities the directory holds for the given internal user
    /// identifiers. An identifier the directory does not know is left out
    /// rather than reported, so a caller sees only users who have signed in.
    /// </summary>
    Task<IReadOnlyCollection<UserIdentity>> FindByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Users whose display name or e-mail address starts with
    /// <paramref name="query"/>, case-insensitively, capped at
    /// <paramref name="limit"/> entries.
    /// </summary>
    /// <remarks>
    /// A prefix rather than a contains match: an anchored comparison is
    /// answered from an index, while a substring search would read every
    /// document in the collection. Only users who have signed in at least once
    /// are in the directory, so only they can be found.
    /// </remarks>
    Task<IReadOnlyCollection<UserSearchMatch>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default);
}
