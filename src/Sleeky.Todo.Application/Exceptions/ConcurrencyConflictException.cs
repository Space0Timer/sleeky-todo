namespace Sleeky.Todo.Application.Exceptions;

public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(
        string resourceName,
        Guid resourceId,
        long expectedVersion)
        : base($"{resourceName} '{resourceId}' is no longer at expected version {expectedVersion}.")
    {
        ResourceName = resourceName;
        ResourceId = resourceId;
        ExpectedVersion = expectedVersion;
    }

    public long ExpectedVersion { get; }

    public Guid ResourceId { get; }

    public string ResourceName { get; }
}
