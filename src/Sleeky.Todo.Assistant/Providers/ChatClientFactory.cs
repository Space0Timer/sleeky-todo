using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Sockets;

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

    /// <summary>
    /// The transport every user-supplied endpoint is reached through.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SharedTransport"/> rather than a flag on it,
    /// because a connect callback belongs to a handler and the application's own
    /// endpoint is deliberately not subject to one. Both are process-lifetime
    /// for the reason described above.
    /// </remarks>
    private static readonly HttpClient GuardedTransport = new HttpClient(
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = GuardedConnectAsync,
        },
        disposeHandler: true)
    {
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

        HttpClient transport = this.TransportFor(connection);

        IChatClient client = connection.Provider switch
        {
            AssistantProvider.Anthropic => this.CreateAnthropic(connection, transport),
            AssistantProvider.OpenAiCompatible => CreateOpenAiCompatible(connection, transport),
            _ => throw new NotSupportedException(
                $"'{connection.Provider}' is not a supported assistant provider."),
        };

        return client;
    }

    /// <summary>
    /// Opens a connection only once the address on the other end has been seen.
    /// </summary>
    /// <remarks>
    /// This is the check that holds, rather than the one on the settings form.
    /// A name that resolved to a public address when it was saved is free to
    /// resolve to a private one by the time a request is made, and a provider
    /// that answers with a redirect chooses its own next host — both arrive
    /// here, because every connection the handler opens goes through this
    /// callback.
    ///
    /// The socket is pointed at the addresses that were just judged rather than
    /// at the host name again. Handing the name back to the stack would resolve
    /// it a second time and connect to whatever that lookup returned, which is
    /// the gap this is written to close.
    /// </remarks>
    private static async ValueTask<Stream> GuardedConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        IPAddress[] resolved = await Dns
            .GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken)
            .ConfigureAwait(false);
        IPAddress[] permitted = Array.FindAll(
            resolved,
            address => !PrivateNetworkPolicy.IsBlocked(address));

        if (permitted.Length == 0)
        {
            throw new EndpointBlockedException(context.DnsEndPoint.Host);
        }

        Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };

        try
        {
            await socket
                .ConnectAsync(permitted, context.DnsEndPoint.Port, cancellationToken)
                .ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static IChatClient CreateOpenAiCompatible(
        AssistantConnection connection,
        HttpClient transport)
    {
        // The base URL is what makes one provider type reach OpenRouter,
        // Ollama, vLLM, and LM Studio; unset, it is OpenAI itself.
        OpenAIClientOptions clientOptions = new OpenAIClientOptions
        {
            // Supplied for the same reason the Anthropic client is given one: a
            // client built per turn against the default transport strands a
            // connection pool each time. It is also what puts this provider
            // behind the same connection guard as the other.
            Transport = new HttpClientPipelineTransport(transport),
        };

        if (connection.BaseUrl is not null)
        {
            clientOptions.Endpoint = connection.BaseUrl;
        }

        OpenAIClient client = new OpenAIClient(
            new ApiKeyCredential(connection.ApiKey),
            clientOptions);

        return client.GetChatClient(connection.Model).AsIChatClient();
    }

    /// <summary>
    /// Picks the transport a connection is made through.
    /// </summary>
    /// <remarks>
    /// The application's own endpoint comes from configuration an operator
    /// wrote, so it is left unguarded — a deployment pointing the assistant at a
    /// model on its own network is a supported arrangement, not an attack. Only
    /// an endpoint that arrived from a user is held to the policy, and the
    /// configuration flag exists for the deployment where those are trusted too,
    /// such as a developer running Ollama on the loopback interface.
    /// </remarks>
    private HttpClient TransportFor(AssistantConnection connection)
    {
        bool guard = connection.Source == AssistantConnectionSource.User
            && !this.options.AllowPrivateEndpoints;

        return guard ? GuardedTransport : SharedTransport;
    }

    private IChatClient CreateAnthropic(
        AssistantConnection connection,
        HttpClient transport)
    {
        ClientOptions clientOptions = new ClientOptions
        {
            ApiKey = connection.ApiKey,
            HttpClient = transport,
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
