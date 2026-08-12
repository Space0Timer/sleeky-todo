using Microsoft.Extensions.Logging;

namespace Sleeky.Todo.IntegrationTests.Api;

internal sealed class ApiExceptionTestLogger<T> : ILogger<T>
{
    public List<ApiExceptionLogEntry> Entries { get; } = new List<ApiExceptionLogEntry>();

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
        Dictionary<string, object?> properties = state
            is IEnumerable<KeyValuePair<string, object?>> structuredState
                ? structuredState.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
        Entries.Add(new ApiExceptionLogEntry(
            logLevel,
            eventId.Id,
            exception,
            properties));
    }
}
