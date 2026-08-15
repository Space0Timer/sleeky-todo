namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// Thrown when a provider endpoint resolved to an address inside the network the
/// application runs in, and the connection was abandoned before it was made.
/// </summary>
/// <remarks>
/// A type of its own rather than a bare <see cref="HttpRequestException"/>, so
/// the probe can tell the user their endpoint was refused on purpose instead of
/// reporting it the same way it reports a host that is merely unreachable.
/// </remarks>
public sealed class EndpointBlockedException : Exception
{
    public EndpointBlockedException(string host)
        : base($"The endpoint '{host}' resolves to a private or loopback address.")
    {
        Host = host;
    }

    public EndpointBlockedException()
        : base("The endpoint resolves to a private or loopback address.")
    {
        Host = string.Empty;
    }

    public EndpointBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Host = string.Empty;
    }

    public string Host { get; }
}
