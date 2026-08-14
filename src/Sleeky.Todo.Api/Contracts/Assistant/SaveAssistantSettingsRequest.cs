using Sleeky.Todo.Assistant.Providers;

namespace Sleeky.Todo.Api.Contracts.Assistant;

/// <summary>
/// A settings save.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> is write-only and optional. Omitting it keeps the
/// stored key, which is the only way to edit a model or an endpoint without
/// re-entering a credential — there is no route that hands the key back.
/// </remarks>
public sealed class SaveAssistantSettingsRequest
{
    public AssistantProvider Provider { get; init; }

    public string? BaseUrl { get; init; }

    public string Model { get; init; } = string.Empty;

    public string? ApiKey { get; init; }
}
