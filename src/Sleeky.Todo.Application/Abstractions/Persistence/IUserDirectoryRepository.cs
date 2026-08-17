using Sleeky.Todo.Application.Abstractions.Identity;

namespace Sleeky.Todo.Application.Abstractions.Persistence;

public interface IUserDirectoryRepository
{
    /// <summary>
    /// Maps an external issuer and subject pair onto a stable internal user
    /// identifier, creating the mapping on first login.
    /// </summary>
    Task<UserIdentity> ResolveAsync(
        string issuer,
        string subject,
        string? displayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The identities the directory holds for the given internal user
    /// identifiers. An identifier the directory does not know is left out
    /// rather than reported, so a caller sees only users who have signed in.
    /// </summary>
    Task<IReadOnlyCollection<UserIdentity>> FindByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}
