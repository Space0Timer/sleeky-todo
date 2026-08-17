using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.IntegrationTests;

/// <summary>
/// An already-bound scope, standing in for what the access behavior binds, so
/// an infrastructure suite can exercise the Space filter without running a
/// request through the pipeline.
/// </summary>
internal sealed class TestSpaceScope : ISpaceScope
{
    public TestSpaceScope(
        Guid spaceId,
        string name = "Test Space",
        SpacePermission permission = SpacePermission.Owner)
    {
        SpaceId = spaceId;
        SpaceName = name;
        Permission = permission;
    }

    public bool IsBound => true;

    public Guid SpaceId { get; }

    public string SpaceName { get; }

    public SpacePermission Permission { get; }
}
