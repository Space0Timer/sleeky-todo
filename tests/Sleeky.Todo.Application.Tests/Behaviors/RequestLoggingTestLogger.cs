using Microsoft.Extensions.Logging;

namespace Sleeky.Todo.Application.Tests.Behaviors;

internal sealed class RequestLoggingTestLogger<T> : ILogger<T>
{
    public List<RequestLoggingLogEntry> Entries { get; } = new List<RequestLoggingLogEntry>();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new RequestLoggingLogEntry(
            logLevel,
            eventId.Id,
            formatter(state, exception)));
    }
}
