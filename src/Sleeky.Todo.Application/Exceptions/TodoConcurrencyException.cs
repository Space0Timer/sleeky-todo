namespace Sleeky.Todo.Application.Exceptions;

public sealed class TodoConcurrencyException : Exception
{
    public TodoConcurrencyException(string todoId, long expectedVersion)
        : base($"TODO '{todoId}' is no longer at expected version {expectedVersion}.")
    {
        TodoId = todoId;
        ExpectedVersion = expectedVersion;
    }

    public string TodoId { get; }

    public long ExpectedVersion { get; }
}
