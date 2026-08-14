namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// What a user is allowed to read back about their own configuration.
/// </summary>
/// <remarks>
/// There is no key on this type, and no endpoint returns one. A key can be
/// replaced but never retrieved, which means a stolen session cannot be used to
/// walk away with the user's credentials.
/// </remarks>
public sealed record AssistantSettingsView(
    string Provider,
    string? BaseUrl,
    string Model,
    bool HasKey,
    bool IsUsable,
    string Source);
