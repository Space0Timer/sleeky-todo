namespace Sleeky.Todo.Application.Exceptions;

public sealed class TodoNotFoundException : Exception
{
    public TodoNotFoundException(string todoId)
        : base($"TODO '{todoId}' was not found.")
    {
        TodoId = todoId;
    }

    public string TodoId { get; }
}
