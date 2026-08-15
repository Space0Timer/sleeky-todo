using System.Net;

namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// Parses the endpoint a self-hosted or proxied provider is reached at.
/// </summary>
/// <remarks>
/// A base URL that will not parse is refused rather than dropped. An unset
/// endpoint means the provider SDK's own default — which for the
/// OpenAI-compatible client is api.openai.com — so ignoring a malformed one
/// would send a key meant for a local model to a third party, with nothing on
/// screen or in the log to say so.
/// </remarks>
public static class AssistantBaseUrl
{
    /// <summary>
    /// Parses an optional endpoint.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> only when a value was supplied and is unusable.
    /// An absent endpoint succeeds with a null <paramref name="parsed"/>,
    /// because most providers are reached at their own default.
    /// </returns>
    public static bool TryParse(string? baseUrl, out Uri? parsed)
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return true;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out Uri? candidate))
        {
            return false;
        }

        // A scheme check rather than parse alone: "localhost:11434/v1" parses
        // as an absolute URI whose scheme is "localhost", which no HTTP client
        // can reach.
        if (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        parsed = candidate;
        return true;
    }

    /// <summary>
    /// Whether an endpoint names an address inside the network the application
    /// runs in, judged without a name lookup.
    /// </summary>
    /// <remarks>
    /// This answers for a literal address only, so it catches the endpoint
    /// somebody typed and reports it on the field they typed it into. A host
    /// name is left alone here on purpose: what it resolves to now is not what
    /// it has to resolve to when the request is finally made, so a name lookup
    /// at this point would report an answer it cannot stand behind. The
    /// connection guard in <see cref="ChatClientFactory"/> is what actually
    /// holds, because it judges the address the socket is about to be opened to.
    /// </remarks>
    public static bool IsPrivate(Uri? baseUrl)
    {
        if (baseUrl is null)
        {
            return false;
        }

        return IPAddress.TryParse(baseUrl.IdnHost, out IPAddress? address)
            && PrivateNetworkPolicy.IsBlocked(address);
    }
}
