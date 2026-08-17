using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Api.Contracts.Spaces;

public sealed class ChangeSpacePermissionRequest
{
    public SpacePermission Permission { get; init; }

    public long Version { get; init; }
}
