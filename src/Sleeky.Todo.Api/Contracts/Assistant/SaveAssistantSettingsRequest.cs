namespace Sleeky.Todo.Api.Contracts.Assistant;

/// <summary>
/// A settings save.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> is write-only and optional. Omitting it keeps the
/// stored key, which is the only way to edit a model or an endpoint without
/// re-entering a credential — there is no route that hands the key back.
///
/// The provider is a name rather than the numeric enum the TODO routes use.
/// Those numbers encode a business ordering; this one is a configuration
/// identifier a person reads and writes, and the settings response already
/// reports it by name — a request that took a number would disagree with the
/// response that follows it.
/// </remarks>
public sealed class SaveAssistantSettingsRequest
{
    public string Provider { get; init; } = string.Empty;

    public string? BaseUrl { get; init; }

    public string Model { get; init; } = string.Empty;

    public string? ApiKey { get; init; }
}
