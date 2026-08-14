namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// A save. <see cref="ApiKey"/> is null when the user is editing the model or
/// the endpoint and leaving their key alone — the only way to change a key is
/// to supply a new one, and there is no way to read the old one back.
/// </summary>
public sealed record AssistantSettingsInput(
    AssistantProvider Provider,
    string? BaseUrl,
    string Model,
    string? ApiKey);
