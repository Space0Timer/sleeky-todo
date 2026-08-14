namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// Everything needed to build a client, with the key in plaintext.
/// </summary>
/// <remarks>
/// Deliberately not a record: the generated <c>ToString</c> would print every
/// property, and one interpolation of this type into a log message would put a
/// user's key in the log. <see cref="ToString"/> is overridden for the same
/// reason.
/// </remarks>
public sealed class AssistantConnection
{
    public AssistantConnection(
        AssistantProvider provider,
        string model,
        string apiKey,
        Uri? baseUrl,
        AssistantConnectionSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        Provider = provider;
        Model = model;
        ApiKey = apiKey;
        BaseUrl = baseUrl;
        Source = source;
    }

    public AssistantProvider Provider { get; }

    public string Model { get; }

    public string ApiKey { get; }

    public Uri? BaseUrl { get; }

    public AssistantConnectionSource Source { get; }

    public override string ToString()
    {
        return $"{Provider}/{Model} ({Source})";
    }
}
