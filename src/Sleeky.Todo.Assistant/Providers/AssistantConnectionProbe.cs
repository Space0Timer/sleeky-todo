using Microsoft.Extensions.AI;

namespace Sleeky.Todo.Assistant.Providers;

public sealed class AssistantConnectionProbe : IAssistantConnectionProbe
{
    private const string Redacted = "***";

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

        // A probe exists to turn any failure into a report the user can act on.
        // Every provider SDK, and every proxy in front of one, fails in its own
        // way — a wrong key, an unknown model, an unreachable host, a TLS
        // refusal — and narrowing this would turn some of those into a 500 that
        // says nothing. Cancellation is re-thrown so a caller that left is not
        // reported as a broken provider.
        try
        {
            await client.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, "Reply with OK.") },
                new ChatOptions { MaxOutputTokens = 16 },
                cancellationToken);

            return new AssistantProbeResult(Succeeded: true, Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new AssistantProbeResult(
                Succeeded: false,
                Scrub(exception.Message, connection.ApiKey));
        }
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
