using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Api.Contracts.Spaces;

public sealed class AddSpaceAccessRequest
{
    public Guid SubjectId { get; init; }

    public SpacePermission Permission { get; init; }

    public long Version { get; init; }
}
