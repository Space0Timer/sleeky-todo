namespace Sleeky.Todo.Application.Exceptions;

/// <summary>
/// Raised when a unit of work is rolled back because a concurrent change
/// conflicted with it. The conflicting document is not always the one the
/// caller named, so no resource identifier is reported.
/// </summary>
public sealed class TransactionConflictException : Exception
{
    public TransactionConflictException(string message)
        : base(message)
    {
    }

    public TransactionConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
