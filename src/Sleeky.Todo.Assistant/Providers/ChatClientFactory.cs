using System.ClientModel;

using Anthropic;
using Anthropic.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

using OpenAI;

namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// Turns a resolved connection into an <see cref="IChatClient"/>.
/// </summary>
/// <remarks>
/// The two adapters end at the same interface, which is what keeps correctness
/// independent of the provider: a model only proposes tool calls, and the
/// server's version binding and domain guards decide. A weaker model degrades
/// helpfulness, never correctness.
/// </remarks>
public sealed class ChatClientFactory : IChatClientFactory
{
    /// <summary>
    /// Shared by every Anthropic client this factory builds, and deliberately
    /// never disposed.
    /// </summary>
    /// <remarks>
    /// A turn builds a client and the adapter returned by <c>AsIChatClient</c>
    /// does not dispose the client underneath it, so without this each turn
    /// would strand an <see cref="AnthropicClient"/> holding its own connection
    /// pool — sockets accumulating in <c>TIME_WAIT</c> until the host runs out
    /// of ephemeral ports. Supplying one handler keeps the per-turn client a
    /// small object the collector can reclaim.
    ///
    /// <c>PooledConnectionLifetime</c> is what makes a process-lifetime handler
    /// safe: connections are recycled, so a provider that moves behind DNS is
    /// picked up rather than pinned until restart.
    /// </remarks>
    private static readonly HttpClient SharedTransport = new HttpClient(
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        },
        disposeHandler: true)
    {
        // Generous rather than absent: a turn is bounded by the caller's
        // cancellation token, and this is only the backstop for a provider that
        // accepts a connection and then says nothing at all.
        Timeout = TimeSpan.FromMinutes(10),
    };

    private readonly AssistantOptions options;

    public ChatClientFactory(IOptions<AssistantOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.options = options.Value;
    }

    public IChatClient Create(AssistantConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        IChatClient client = connection.Provider switch
        {
            AssistantProvider.Anthropic => this.CreateAnthropic(connection),
            AssistantProvider.OpenAiCompatible => CreateOpenAiCompatible(connection),
            _ => throw new NotSupportedException(
                $"'{connection.Provider}' is not a supported assistant provider."),
        };

        return client;
    }

    private static IChatClient CreateOpenAiCompatible(AssistantConnection connection)
    {
        // The base URL is what makes one provider type reach OpenRouter,
        // Ollama, vLLM, and LM Studio; unset, it is OpenAI itself.
        OpenAIClientOptions clientOptions = new OpenAIClientOptions();

        if (connection.BaseUrl is not null)
        {
            clientOptions.Endpoint = connection.BaseUrl;
        }

        OpenAIClient client = new OpenAIClient(
            new ApiKeyCredential(connection.ApiKey),
            clientOptions);

        return client.GetChatClient(connection.Model).AsIChatClient();
    }

    private IChatClient CreateAnthropic(AssistantConnection connection)
    {
        ClientOptions clientOptions = new ClientOptions
        {
            ApiKey = connection.ApiKey,
            HttpClient = SharedTransport,
        };

        if (connection.BaseUrl is not null)
        {
            clientOptions.BaseUrl = connection.BaseUrl.ToString();
        }

        // The cap covers thinking and the answer together on current models, so
        // it is sized for both rather than for the reply alone.
        return new AnthropicClient(clientOptions)
            .AsIChatClient(connection.Model, this.options.AnthropicMaxTokens);
    }
}
