using Microsoft.Extensions.Logging;

namespace Sleeky.Todo.Application.Tests.Behaviors;

internal sealed class RequestLoggingLogEntry
{
    public RequestLoggingLogEntry(LogLevel level, int eventId, string message)
    {
        Level = level;
        EventId = eventId;
        Message = message;
    }

    public int EventId { get; }

    public LogLevel Level { get; }

    public string Message { get; }
}
