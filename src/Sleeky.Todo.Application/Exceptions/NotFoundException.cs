namespace Sleeky.Todo.Application.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string resourceName, string resourceId)
        : base($"{resourceName} '{resourceId}' was not found.")
    {
        ResourceName = resourceName;
        ResourceId = resourceId;
    }

    public string ResourceId { get; }

    public string ResourceName { get; }
}
