namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// The application-level fallback, used when a user has not brought their own
/// key. Leaving <see cref="ApiKey"/> unset simply means every user must bring
/// one, which is the deployment where the app carries no token cost at all.
/// </summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    public AssistantProvider Provider { get; init; } = AssistantProvider.Anthropic;

    public string Model { get; init; } = "claude-sonnet-5";

    public string? ApiKey { get; init; }

    public string? BaseUrl { get; init; }

    /// <summary>
    /// Sized for thinking plus the answer, because thinking is on by default on
    /// current Anthropic models and shares this cap: an answer-sized budget
    /// truncates mid-response. Other providers keep their own defaults and
    /// ignore this.
    /// </summary>
    public int AnthropicMaxTokens { get; init; } = 8192;
}
