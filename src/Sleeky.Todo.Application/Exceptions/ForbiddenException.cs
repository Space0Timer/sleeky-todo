namespace Sleeky.Todo.Application.Exceptions;

/// <summary>
/// The caller can see the resource but does not hold the level the operation
/// needs. Distinct from <see cref="NotFoundException"/>, which is what a
/// caller with no access at all receives, so a refusal never confirms a
/// resource the caller could not otherwise see.
/// </summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(
        string resourceName,
        Guid resourceId,
        string requiredPermission)
        : base($"{resourceName} '{resourceId}' requires {requiredPermission} permission.")
    {
        ResourceName = resourceName;
        ResourceId = resourceId;
        RequiredPermission = requiredPermission;
    }

    public string RequiredPermission { get; }

    public Guid ResourceId { get; }

    public string ResourceName { get; }
}
