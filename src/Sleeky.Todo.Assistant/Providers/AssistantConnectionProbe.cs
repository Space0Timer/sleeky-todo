using System.Net.Sockets;
using System.Security.Authentication;

using Microsoft.Extensions.AI;

namespace Sleeky.Todo.Assistant.Providers;

public sealed class AssistantConnectionProbe : IAssistantConnectionProbe
{
    private const string Redacted = "***";

    /// <summary>
    /// Every way of failing to reach a host, reported as one thing.
    /// </summary>
    /// <remarks>
    /// The distinctions this discards are the point. A refused connection, a
    /// name that does not resolve, a TLS handshake that is rejected and a host
    /// that accepts and then says nothing are four different messages, and a
    /// caller who can tell them apart can read the shape of the network behind
    /// this server one endpoint at a time. What remains is what the user needs:
    /// the endpoint they typed did not answer.
    ///
    /// This is second to the connection guard rather than a replacement for it.
    /// The guard is what makes an internal address unreachable; this is what
    /// keeps the attempt from being informative.
    /// </remarks>
    private const string Unreachable =
        "The endpoint could not be reached. Check the base URL and that the "
        + "provider is running and accepting requests.";

    /// <summary>
    /// A probe is a single short request, so it does not wait the way a turn
    /// does. Without this it inherits the transport's timeout and an address
    /// that accepts a connection and then stalls holds the request for minutes.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IChatClientFactory clients;

    public AssistantConnectionProbe(IChatClientFactory clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        this.clients = clients;
    }

    public async Task<AssistantProbeResult> ProbeAsync(
        AssistantConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using IChatClient client = this.clients.Create(connection);
        using CancellationTokenSource bounded = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(Timeout);

        // A probe exists to turn any failure into a report the user can act on.
        // Every provider SDK, and every proxy in front of one, fails in its own
        // way — a wrong key, an unknown model, an unreachable host, a TLS
        // refusal — and narrowing this would turn some of those into a 500 that
        // says nothing. Cancellation by the caller is re-thrown so a caller that
        // left is not reported as a broken provider.
        try
        {
            await client.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, "Reply with OK.") },
                new ChatOptions { MaxOutputTokens = 16 },
                bounded.Token);

            return new AssistantProbeResult(Succeeded: true, Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new AssistantProbeResult(
                Succeeded: false,
                Describe(exception, connection.ApiKey));
        }
    }

    /// <summary>
    /// Turns a failure into something the user can act on without letting it
    /// report on the network this server sits in.
    /// </summary>
    /// <remarks>
    /// A provider that answered is quoted, scrubbed. Anything that failed before
    /// an answer is collapsed, because that is the class of failure whose detail
    /// describes the network rather than the configuration.
    /// </remarks>
    private static string Describe(Exception exception, string apiKey)
    {
        // The whole chain is searched for a refusal before any part of it is
        // judged a transport fault, rather than both tests being applied at each
        // link. The handler wraps whatever a connect callback throws in an
        // HttpRequestException, so a refusal always sits underneath an exception
        // the transport test matches — and testing link by link would report
        // every blocked endpoint as an ordinary unreachable one.
        EndpointBlockedException? blocked = FindInChain<EndpointBlockedException>(exception);

        if (blocked is not null)
        {
            // Reported plainly. The user named this address themselves, so
            // saying it was refused tells them nothing they did not supply, and
            // letting it fall through would explain a policy decision as a
            // network fault.
            return blocked.Message;
        }

        return IsTransportFailure(exception)
            ? Unreachable
            : Scrub(exception.Message, apiKey);
    }

    private static TException? FindInChain<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }

    private static bool IsTransportFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException
                or SocketException
                or AuthenticationException
                or IOException
                or OperationCanceledException
                or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Providers have been known to quote the credential back inside an error.
    /// The message is shown to the user and may be logged by whatever handles
    /// the response, so the key is removed before it can travel.
    /// </summary>
    private static string Scrub(string message, string apiKey)
    {
        return string.IsNullOrEmpty(apiKey)
            ? message
            : message.Replace(apiKey, Redacted, StringComparison.Ordinal);
    }
}
