using System.Net.Sockets;
using System.Security.Authentication;

using FluentAssertions;

using Microsoft.Extensions.AI;

using Sleeky.Todo.Assistant.Providers;

namespace Sleeky.Todo.Assistant.Tests.Providers;

[TestClass]
public sealed class AssistantConnectionProbeTests
{
    private const string Key = "sk-probe-secret-value";

    /// <summary>
    /// Four distinct exceptions carrying four distinct messages, each of which
    /// would otherwise describe the network rather than the configuration.
    /// </summary>
    public static IEnumerable<object[]> TransportFailures =>
    [
        [new HttpRequestException("No such host is known.")],
        [new HttpRequestException("Connection refused (10.0.0.5:6379)")],
        [new SocketException(10061)],
        [new AuthenticationException("The remote certificate is invalid.")],
        [new IOException("The response ended prematurely.")],
    ];

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
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Every way of failing to reach a host reports as one thing.
    /// </summary>
    /// <remarks>
    /// A caller who can tell a refused connection from a name that does not
    /// resolve, or from a TLS handshake that is rejected, can read the shape of
    /// the network behind this server one endpoint at a time. These are four
    /// distinct exceptions carrying four distinct messages, and the assertion
    /// that matters is that none of it survives to the caller.
    /// </remarks>
    [TestMethod]
    [DynamicData(nameof(TransportFailures))]
    public async Task ProbeReportsEveryUnreachableEndpointIdentically(Exception failure)
    {
        AssistantConnectionProbe probe = new AssistantConnectionProbe(
            new StubChatClientFactory(new StubChatClient(failure)));

        AssistantProbeResult result = await probe.ProbeAsync(Connection());

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(
            "The endpoint could not be reached. Check the base URL and that the "
            + "provider is running and accepting requests.");
        result.Error.Should().NotContain(failure.Message);
    }

    /// <summary>
    /// An endpoint the policy refused is reported as refused rather than as
    /// unreachable, because the user named that address themselves and a
    /// network-fault message would explain a decision as a failure.
    /// </summary>
    [TestMethod]
    public async Task ProbeReportsABlockedEndpointAsBlocked()
    {
        AssistantConnectionProbe probe = new AssistantConnectionProbe(
            new StubChatClientFactory(new StubChatClient(
                new HttpRequestException(
                    "outer",
                    new EndpointBlockedException("169.254.169.254")))));

        AssistantProbeResult result = await probe.ProbeAsync(Connection());

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("private or loopback");
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
