namespace Sleeky.Todo.Domain.Exceptions;

public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// A rule violation surfaced by a lower-level failure, which is kept as the
    /// inner exception so the cause is not lost in translation.
    /// </summary>
    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
