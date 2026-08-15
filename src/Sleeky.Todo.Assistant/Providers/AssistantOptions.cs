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
    /// Whether a user may point the assistant at an address inside the network
    /// the application runs in.
    /// </summary>
    /// <remarks>
    /// Off by default, because the request is made by the server: left on, an
    /// ordinary account can use the settings form to reach anything the
    /// container can reach and read the outcome back off the test button.
    ///
    /// Development turns it on, where the endpoint worth naming is a model on
    /// the loopback interface — <c>http://localhost:11434/v1</c> for Ollama —
    /// and the network on the other side of the guard is the developer's own.
    /// This never applies to <see cref="BaseUrl"/>, which an operator sets.
    /// </remarks>
    public bool AllowPrivateEndpoints { get; init; }

    /// <summary>
    /// Sized for thinking plus the answer, because thinking is on by default on
    /// current Anthropic models and shares this cap: an answer-sized budget
    /// truncates mid-response. Other providers keep their own defaults and
    /// ignore this.
    /// </summary>
    public int AnthropicMaxTokens { get; init; } = 8192;

    /// <summary>
    /// How many messages of a conversation are replayed, counting the model's
    /// tool calls and their results rather than only what was typed.
    /// </summary>
    /// <remarks>
    /// An exchange that reads before it answers costs about four, so the
    /// default holds roughly a dozen of them. Raising it buys the model a
    /// longer memory and charges every later turn for it; setting it to zero or
    /// less replays everything, which is the behaviour this bound replaced.
    /// </remarks>
    public int TranscriptMaxMessages { get; init; } = 60;
}
