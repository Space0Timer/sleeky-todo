using FluentAssertions;

using Microsoft.Extensions.AI;

using Sleeky.Todo.Assistant.Providers;

namespace Sleeky.Todo.Assistant.Tests.Providers;

[TestClass]
public sealed class AssistantConnectionProbeTests
{
    private const string Key = "sk-probe-secret-value";

    [TestMethod]
    public async Task ProbeReportsSuccessWhenTheProviderAnswers()
    {
        AssistantConnectionProbe probe = new AssistantConnectionProbe(
            new StubChatClientFactory(new StubChatClient(failure: null)));

        AssistantProbeResult result = await probe.ProbeAsync(Connection());

        result.Succeeded.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    /// <summary>
    /// A probe exists to turn any failure into something the user can act on,
    /// so it reports rather than throws however the provider fails.
    /// </summary>
    [TestMethod]
    public async Task ProbeReportsAFailureInsteadOfThrowing()
    {
        AssistantConnectionProbe probe = new AssistantConnectionProbe(
            new StubChatClientFactory(new StubChatClient(
                new HttpRequestException("No such host is known."))));

        AssistantProbeResult result = await probe.ProbeAsync(Connection());

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("No such host is known.");
    }

    /// <summary>
    /// Providers have been known to quote the credential back inside an error.
    /// The message is shown to the user and may be logged by whatever handles
    /// the response, so the key must not survive the trip.
    /// </summary>
    [TestMethod]
    public async Task ProbeStripsTheKeyOutOfWhateverTheProviderSaid()
    {
        AssistantConnectionProbe probe = new AssistantConnectionProbe(
            new StubChatClientFactory(new StubChatClient(
                new InvalidOperationException($"Rejected credential '{Key}' for model m."))));

        AssistantProbeResult result = await probe.ProbeAsync(Connection());

        result.Succeeded.Should().BeFalse();
        result.Error.Should().NotContain(Key);
        result.Error.Should().Contain("Rejected credential").And.Contain("***");
    }

    /// <summary>
    /// A caller that left is not a broken provider, so cancellation is not
    /// reported as one.
    /// </summary>
    [TestMethod]
    public async Task ProbeLetsCancellationThrough()
    {
        using CancellationTokenSource cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        AssistantConnectionProbe probe = new AssistantConnectionProbe(
            new StubChatClientFactory(new StubChatClient(
                new OperationCanceledException())));

        Func<Task> act = async () => await probe.ProbeAsync(Connection(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static AssistantConnection Connection()
    {
        return new AssistantConnection(
            AssistantProvider.Anthropic,
            "claude-sonnet-5",
            Key,
            baseUrl: null,
            AssistantConnectionSource.User);
    }

    private sealed class StubChatClientFactory : IChatClientFactory
    {
        private readonly IChatClient client;

        public StubChatClientFactory(IChatClient client)
        {
            this.client = client;
        }

        public IChatClient Create(AssistantConnection connection) => this.client;
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly Exception? failure;

        public StubChatClient(Exception? failure)
        {
            this.failure = failure;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (this.failure is not null)
            {
                throw this.failure;
            }

            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "OK")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The probe does not stream.");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
