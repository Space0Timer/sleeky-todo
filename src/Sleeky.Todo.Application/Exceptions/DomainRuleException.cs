namespace Sleeky.Todo.Application.Exceptions;

public sealed class DomainRuleException : Exception
{
    public DomainRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
