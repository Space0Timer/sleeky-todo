using Microsoft.Extensions.Logging;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.ChangeTodoStatus;

internal sealed class LogEntry
{
    public LogEntry(
        LogLevel level,
        int eventId,
        IReadOnlyDictionary<string, object?> properties,
        bool transactionCompleted)
    {
        Level = level;
        EventId = eventId;
        Properties = properties;
        TransactionCompleted = transactionCompleted;
    }

    public int EventId { get; }

    public LogLevel Level { get; }

    public IReadOnlyDictionary<string, object?> Properties { get; }

    public bool TransactionCompleted { get; }
}
