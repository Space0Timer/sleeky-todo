namespace Sleeky.Todo.Application.Exceptions;

public sealed class InvalidCursorException : Exception
{
    public InvalidCursorException(string message)
        : base(message)
    {
    }

    public InvalidCursorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
