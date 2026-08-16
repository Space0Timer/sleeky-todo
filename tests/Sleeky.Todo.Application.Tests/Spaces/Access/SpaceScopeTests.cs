using FluentAssertions;

using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Spaces.Access;

[TestClass]
public sealed class SpaceScopeTests
{
    private static readonly Guid SpaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherSpaceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// The scope fails closed: a repository reading it before any check has
    /// passed gets an exception, not an empty identifier that would match
    /// nothing — or, worse, a filter it might be tempted to leave out.
    /// </summary>
    [TestMethod]
    public void AnUnboundScopeRefusesEveryRead()
    {
        SpaceScope scope = new SpaceScope();

        scope.IsBound.Should().BeFalse();
        FluentActions.Invoking(() => scope.SpaceId).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => scope.SpaceName).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => scope.Permission).Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void BindExposesTheContext()
    {
        SpaceScope scope = new SpaceScope();

        scope.Bind(new SpaceAccessContext(SpaceId, "Project Alpha", SpacePermission.Write));

        scope.IsBound.Should().BeTrue();
        scope.SpaceId.Should().Be(SpaceId);
        scope.SpaceName.Should().Be("Project Alpha");
        scope.Permission.Should().Be(SpacePermission.Write);
    }

    /// <summary>
    /// The same Space checked twice in one request — the assistant's turn-level
    /// check followed by each tool's command — simply refreshes the binding.
    /// </summary>
    [TestMethod]
    public void RebindingTheSameSpaceReplacesTheContext()
    {
        SpaceScope scope = new SpaceScope();
        scope.Bind(new SpaceAccessContext(SpaceId, "Project Alpha", SpacePermission.Read));

        scope.Bind(new SpaceAccessContext(SpaceId, "Project Alpha", SpacePermission.Owner));

        scope.Permission.Should().Be(SpacePermission.Owner);
    }

    [TestMethod]
    public void RebindingADifferentSpaceIsRefused()
    {
        SpaceScope scope = new SpaceScope();
        scope.Bind(new SpaceAccessContext(SpaceId, "Project Alpha", SpacePermission.Read));

        Action act = () =>
            scope.Bind(new SpaceAccessContext(OtherSpaceId, "Marketing", SpacePermission.Read));

        act.Should().Throw<InvalidOperationException>();
        scope.SpaceId.Should().Be(SpaceId);
    }

    [TestMethod]
    public void BindRejectsNull()
    {
        SpaceScope scope = new SpaceScope();

        Action act = () => scope.Bind(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
