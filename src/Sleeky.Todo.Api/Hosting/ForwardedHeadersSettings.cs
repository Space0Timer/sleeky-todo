namespace Sleeky.Todo.Api.Hosting;

/// <summary>
/// Identifies the proxies whose forwarded headers this host will believe. The
/// headers decide the scheme and host the OpenID Connect redirect URI is built
/// from, so accepting them from an untrusted caller lets that caller choose
/// where the provider returns the browser.
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Gets the addresses of trusted proxies. Loopback is trusted by default,
    /// which covers the development proxy but not a sidecar or ingress on its
    /// own address.
    /// </summary>
    public string[] KnownProxies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets trusted proxy networks in CIDR notation, for deployments where the
    /// proxy's address is assigned rather than fixed.
    /// </summary>
    public string[] KnownNetworks { get; init; } = Array.Empty<string>();
}
