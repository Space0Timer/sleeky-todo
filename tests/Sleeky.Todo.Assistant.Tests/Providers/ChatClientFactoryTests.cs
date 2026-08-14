using FluentAssertions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

using Sleeky.Todo.Assistant.Providers;

namespace Sleeky.Todo.Assistant.Tests.Providers;

/// <summary>
/// These assert the endpoint a built client will actually talk to, which is the
/// one thing about this class that can go wrong silently. An unset endpoint
/// means the provider SDK's own default, so a base URL that fails to reach the
/// client does not fail loudly — it sends the user's key somewhere else.
/// </summary>
[TestClass]
public sealed class ChatClientFactoryTests
{
    private const string Key = "sk-factory-test";

    [TestMethod]
    public void CreateSendsAnOpenAiCompatibleClientToTheConfiguredEndpoint()
    {
        using IChatClient client = Factory().Create(Connection(
            AssistantProvider.OpenAiCompatible,
            "llama-3",
            new Uri("http://localhost:11434/v1")));

        Metadata(client).ProviderUri.Should().Be(new Uri("http://localhost:11434/v1"));
    }

    /// <summary>
    /// The default this exists to make visible: with no endpoint configured, an
    /// OpenAI-compatible client talks to OpenAI. That is correct, and it is also
    /// why a malformed base URL must be refused rather than dropped.
    /// </summary>
    [TestMethod]
    public void CreateFallsBackToOpenAiWhenNoEndpointIsConfigured()
    {
        using IChatClient client = Factory().Create(Connection(
            AssistantProvider.OpenAiCompatible,
            "gpt-4o-mini",
            baseUrl: null));

        Metadata(client).ProviderUri.Should().Be(new Uri("https://api.openai.com/v1"));
    }

    [TestMethod]
    public void CreateSendsAnAnthropicClientToTheConfiguredEndpoint()
    {
        using IChatClient client = Factory().Create(Connection(
            AssistantProvider.Anthropic,
            "claude-sonnet-5",
            new Uri("http://localhost:9999")));

        Metadata(client).ProviderUri.Should().Be(new Uri("http://localhost:9999/"));
    }

    [TestMethod]
    public void CreateUsesTheAnthropicAdapterAndItsModel()
    {
        using IChatClient client = Factory().Create(Connection(
            AssistantProvider.Anthropic,
            "claude-sonnet-5",
            baseUrl: null));

        ChatClientMetadata metadata = Metadata(client);
        metadata.ProviderName.Should().Be("anthropic");
        metadata.DefaultModelId.Should().Be("claude-sonnet-5");
        metadata.ProviderUri.Should().Be(new Uri("https://api.anthropic.com/"));
    }

    [TestMethod]
    public void CreateRefusesAProviderItDoesNotSupport()
    {
        Func<IChatClient> act = () => Factory().Create(Connection(
            (AssistantProvider)99,
            "whatever",
            baseUrl: null));

        act.Should().Throw<NotSupportedException>().WithMessage("*99*");
    }

    private static ChatClientMetadata Metadata(IChatClient client)
    {
        return client.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata
            ?? throw new InvalidOperationException("The client reported no metadata.");
    }

    private static AssistantConnection Connection(
        AssistantProvider provider,
        string model,
        Uri? baseUrl)
    {
        return new AssistantConnection(
            provider,
            model,
            Key,
            baseUrl,
            AssistantConnectionSource.User);
    }

    private static ChatClientFactory Factory()
    {
        return new ChatClientFactory(Options.Create(new AssistantOptions()));
    }
}
