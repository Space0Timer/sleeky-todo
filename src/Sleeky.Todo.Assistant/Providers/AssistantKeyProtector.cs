using Microsoft.AspNetCore.DataProtection;

namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// The one place a user's API key becomes plaintext.
/// </summary>
/// <remarks>
/// Nothing here logs, and nothing here returns the key to a caller that is not
/// about to build a provider client with it. Keeping that rule enforceable is
/// the reason encryption is not left to persistence: the repository stores a
/// string it cannot read, so no query, projection, or diagnostic dump along
/// that path can expose a usable secret.
///
/// A purpose string binds ciphertext to this use, so a value protected
/// elsewhere in the application cannot be unprotected as a key here.
/// </remarks>
public sealed class AssistantKeyProtector
{
    private const string Purpose = "Sleeky.Todo.Assistant.ApiKey.v1";

    private readonly IDataProtector protector;

    public AssistantKeyProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        this.protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return this.protector.Protect(apiKey);
    }

    /// <summary>
    /// Reverses <see cref="Protect"/>.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the ciphertext cannot be read, which is what
    /// a rotated or lost key ring looks like. The stored value is unusable
    /// rather than corrupt, so the caller asks the user to enter the key again
    /// instead of failing the request as an error they cannot act on.
    /// </returns>
    public string? Unprotect(string? protectedApiKey)
    {
        if (string.IsNullOrEmpty(protectedApiKey))
        {
            return null;
        }

        try
        {
            return this.protector.Unprotect(protectedApiKey);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
