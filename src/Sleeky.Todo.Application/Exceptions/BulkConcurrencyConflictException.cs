namespace Sleeky.Todo.Application.Exceptions;

/// <summary>
/// Raised when a batch write does not apply in full. The batch is abandoned
/// rather than partially applied, so callers retry the whole read-modify-write.
/// </summary>
public sealed class BulkConcurrencyConflictException : Exception
{
    public BulkConcurrencyConflictException(
        string resourceName,
        IReadOnlyCollection<Guid> resourceIds)
        : base(BuildMessage(resourceName, resourceIds))
    {
        ResourceName = resourceName;
        ResourceIds = resourceIds;
    }

    public BulkConcurrencyConflictException(
        string resourceName,
        IReadOnlyCollection<Guid> resourceIds,
        Exception innerException)
        : base(BuildMessage(resourceName, resourceIds), innerException)
    {
        ResourceName = resourceName;
        ResourceIds = resourceIds;
    }

    public IReadOnlyCollection<Guid> ResourceIds { get; }

    public string ResourceName { get; }

    private static string BuildMessage(
        string resourceName,
        IReadOnlyCollection<Guid> resourceIds)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);

        return resourceIds.Count == 0
            ? $"A concurrent change prevented the {resourceName} batch from being applied."
            : $"A concurrent change prevented the {resourceName} batch from being applied: "
                + string.Join(", ", resourceIds.Select(id => $"'{id}'"))
                + ".";
    }
}
