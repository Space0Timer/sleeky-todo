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
