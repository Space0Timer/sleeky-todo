namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// The provider families the assistant can talk to.
/// </summary>
/// <remarks>
/// Two entries rather than one per vendor: an OpenAI-compatible client with a
/// configurable base URL reaches OpenRouter, Ollama, vLLM, LM Studio, and most
/// self-hosted setups without a type of its own for each. Anthropic stays
/// first-class because its own SDK ships the adapter and its options differ.
/// </remarks>
public enum AssistantProvider
{
    Anthropic = 0,
    OpenAiCompatible = 1,
}
