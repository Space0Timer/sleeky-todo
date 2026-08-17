using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Assistant.Tests;

/// <summary>
/// An already-bound scope, for tests that exercise what runs after the access
/// check rather than the check itself.
/// </summary>
internal sealed class TestSpaceScope : ISpaceScope
{
    public TestSpaceScope(
        Guid spaceId,
        string name = "Test Space",
        SpacePermission permission = SpacePermission.Owner)
    {
        this.SpaceId = spaceId;
        this.SpaceName = name;
        this.Permission = permission;
    }

    public bool IsBound => true;

    public Guid SpaceId { get; }

    public string SpaceName { get; }

    public SpacePermission Permission { get; }
}
