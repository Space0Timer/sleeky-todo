namespace Sleeky.Todo.Application.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string resourceName, Guid resourceId)
        : base($"{resourceName} '{resourceId}' was not found.")
    {
        ResourceName = resourceName;
        ResourceId = resourceId;
    }

    public Guid ResourceId { get; }

    public string ResourceName { get; }
}
