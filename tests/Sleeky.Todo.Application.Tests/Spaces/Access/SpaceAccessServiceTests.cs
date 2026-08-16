using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Spaces.Access;

[TestClass]
public sealed class SpaceAccessServiceTests
{
    private static readonly DateTimeOffset Timestamp = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid SpaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherSpaceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OwnerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid WriterId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ReaderId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid StrangerId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    /// <summary>
    /// A Space that does not exist and a Space the caller cannot see are the
    /// same answer, so neither response confirms the identifier.
    /// </summary>
    [TestMethod]
    public async Task AnUnknownSpaceIsNotFound()
    {
        Harness harness = new Harness(OwnerId);

        Func<Task> act = () => harness.Service.RequireAsync(SpaceId, SpacePermission.Read);

        NotFoundException exception = (await act.Should().ThrowAsync<NotFoundException>()).Which;
        exception.ResourceName.Should().Be("Space");
        exception.ResourceId.Should().Be(SpaceId);
        harness.Scope.IsBound.Should().BeFalse();
    }

    [TestMethod]
    public async Task ANonMemberIsNotFound()
    {
        Harness harness = new Harness(StrangerId, CreateSharedSpace());

        Func<Task> act = () => harness.Service.RequireAsync(SpaceId, SpacePermission.Read);

        await act.Should().ThrowAsync<NotFoundException>();
        harness.Scope.IsBound.Should().BeFalse();
    }

    [TestMethod]
    [DataRow(SpacePermission.Read, SpacePermission.Write)]
    [DataRow(SpacePermission.Read, SpacePermission.Owner)]
    [DataRow(SpacePermission.Write, SpacePermission.Owner)]
    public async Task AMemberBelowTheRequiredLevelIsForbidden(
        SpacePermission held,
        SpacePermission required)
    {
        Harness harness = new Harness(UserHolding(held), CreateSharedSpace());

        Func<Task> act = () => harness.Service.RequireAsync(SpaceId, required);

        ForbiddenException exception = (await act.Should().ThrowAsync<ForbiddenException>()).Which;
        exception.ResourceName.Should().Be("Space");
        exception.ResourceId.Should().Be(SpaceId);
        exception.RequiredPermission.Should().Be(required.ToString());
        harness.Scope.IsBound.Should().BeFalse();
    }

    [TestMethod]
    [DataRow(SpacePermission.Owner, SpacePermission.Read)]
    [DataRow(SpacePermission.Owner, SpacePermission.Write)]
    [DataRow(SpacePermission.Owner, SpacePermission.Owner)]
    [DataRow(SpacePermission.Write, SpacePermission.Read)]
    [DataRow(SpacePermission.Write, SpacePermission.Write)]
    [DataRow(SpacePermission.Read, SpacePermission.Read)]
    public async Task AMemberAtOrAboveTheRequiredLevelPassesAndBindsTheScope(
        SpacePermission held,
        SpacePermission required)
    {
        Harness harness = new Harness(UserHolding(held), CreateSharedSpace());

        SpaceAccessContext context = await harness.Service.RequireAsync(SpaceId, required);

        context.Should().Be(new SpaceAccessContext(SpaceId, "Project Alpha", held));
        harness.Scope.IsBound.Should().BeTrue();
        harness.Scope.SpaceId.Should().Be(SpaceId);
        harness.Scope.SpaceName.Should().Be("Project Alpha");
        harness.Scope.Permission.Should().Be(held);
    }

    /// <summary>
    /// A handler authorized for one Space that dispatches work in another
    /// would otherwise leave the ambient scope pointing at whichever ran last.
    /// </summary>
    [TestMethod]
    public async Task ASecondSpaceInTheSameRequestIsRefused()
    {
        Harness harness = new Harness(OwnerId, CreateSharedSpace(), CreateOtherSpace());
        await harness.Service.RequireAsync(SpaceId, SpacePermission.Read);

        Func<Task> act = () => harness.Service.RequireAsync(OtherSpaceId, SpacePermission.Read);

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Scope.SpaceId.Should().Be(SpaceId);
    }

    [TestMethod]
    public void ConstructorRejectsNullDependencies()
    {
        ISpaceRepository spaces = Substitute.For<ISpaceRepository>();
        TestCurrentUser currentUser = new TestCurrentUser(OwnerId);
        SpaceScope scope = new SpaceScope();

        FluentActions.Invoking(() => new SpaceAccessService(null!, currentUser, scope))
            .Should().Throw<ArgumentNullException>().WithParameterName("spaces");
        FluentActions.Invoking(() => new SpaceAccessService(spaces, null!, scope))
            .Should().Throw<ArgumentNullException>().WithParameterName("currentUser");
        FluentActions.Invoking(() => new SpaceAccessService(spaces, currentUser, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("scope");
    }

    private static Guid UserHolding(SpacePermission permission)
    {
        return permission switch
        {
            SpacePermission.Owner => OwnerId,
            SpacePermission.Write => WriterId,
            SpacePermission.Read => ReaderId,
            _ => throw new ArgumentOutOfRangeException(nameof(permission)),
        };
    }

    private static Space CreateSharedSpace()
    {
        Space space = Space.Create(SpaceId, "Project Alpha", OwnerId, Timestamp);
        space.AddAccess(WriterId, SubjectType.User, SpacePermission.Write, Timestamp);
        space.AddAccess(ReaderId, SubjectType.User, SpacePermission.Read, Timestamp);

        return space;
    }

    private static Space CreateOtherSpace()
    {
        return Space.Create(OtherSpaceId, "Marketing", OwnerId, Timestamp);
    }

    private sealed class Harness
    {
        public Harness(Guid userId, params Space[] storedSpaces)
        {
            ISpaceRepository spaces = Substitute.For<ISpaceRepository>();
            foreach (Space stored in storedSpaces)
            {
                spaces.GetByIdAsync(stored.Id, Arg.Any<CancellationToken>()).Returns(stored);
            }

            Scope = new SpaceScope();
            Service = new SpaceAccessService(spaces, new TestCurrentUser(userId), Scope);
        }

        public SpaceScope Scope { get; }

        public SpaceAccessService Service { get; }
    }
}
