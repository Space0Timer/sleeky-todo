namespace Sleeky.Todo.Application.Abstractions.Identity;

public interface IUserDirectory
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
}
