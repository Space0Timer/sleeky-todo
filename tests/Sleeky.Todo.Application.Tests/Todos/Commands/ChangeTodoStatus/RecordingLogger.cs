using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.ChangeTodoStatus;

internal sealed class RecordingLogger : ILogger<ChangeTodoStatusCommandHandler>
{
    private readonly Func<bool> isTransactionCompleted;

    public RecordingLogger(Func<bool> isTransactionCompleted)
    {
        this.isTransactionCompleted = isTransactionCompleted;
    }

    public List<LogEntry> Entries { get; } = new List<LogEntry>();

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
        Entries.Add(new LogEntry(
            logLevel,
            eventId.Id,
            properties,
            isTransactionCompleted()));
    }
}
