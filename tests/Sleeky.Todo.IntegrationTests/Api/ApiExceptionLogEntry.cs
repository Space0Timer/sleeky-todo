using Microsoft.Extensions.Logging;

namespace Sleeky.Todo.IntegrationTests.Api;

internal sealed class ApiExceptionLogEntry
{
    public ApiExceptionLogEntry(
        LogLevel level,
        int eventId,
        Exception? exception,
        IReadOnlyDictionary<string, object?> properties)
    {
        Level = level;
        EventId = eventId;
        Exception = exception;
        Properties = properties;
    }

    public int EventId { get; }

    public Exception? Exception { get; }

    public LogLevel Level { get; }

    public IReadOnlyDictionary<string, object?> Properties { get; }
}
