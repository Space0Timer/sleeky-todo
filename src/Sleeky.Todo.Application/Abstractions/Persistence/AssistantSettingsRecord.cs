namespace Sleeky.Todo.Application.Abstractions.Persistence;

/// <summary>
/// One user's assistant configuration as it is stored.
/// </summary>
/// <param name="ProtectedApiKey">
/// Ciphertext. The key is encrypted before it reaches persistence and decrypted
/// only where a provider client is built, so a leaked database still requires
/// the data-protection keys to be worth anything, and nothing on this path can
/// log a usable secret.
/// </param>
public sealed record AssistantSettingsRecord(
    Guid UserId,
    string Provider,
    string? BaseUrl,
    string Model,
    string? ProtectedApiKey);
