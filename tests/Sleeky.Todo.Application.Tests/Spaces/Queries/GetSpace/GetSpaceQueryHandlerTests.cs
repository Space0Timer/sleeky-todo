using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Spaces.Queries.GetSpace;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Spaces.Queries.GetSpace;

[TestClass]
public sealed class GetSpaceQueryHandlerTests
{
    [TestMethod]
    [DataRow(SpacePermission.Owner)]
    [DataRow(SpacePermission.Write)]
    [DataRow(SpacePermission.Read)]
    public async Task HandleReturnsTheSpaceWithTheCallersOwnLevelAndNamedMembers(
        SpacePermission held)
    {
        Space space = TestSpaceFactory.CreateShared();
        Guid userId = UserHolding(held);
        IUserDirectoryRepository users = TestSpaceFactory.CreateDirectory(
            (TestSpaceFactory.OwnerId, "Alice"),
            (TestSpaceFactory.WriterId, "Bob"),
            (TestSpaceFactory.ReaderId, "Carol"));
        GetSpaceQueryHandler handler = new GetSpaceQueryHandler(
            TestSpaceFactory.CreateRepository(space),
            users,
            new TestCurrentUser(userId));

        SpaceDto result = await handler.Handle(new GetSpaceQuery(space.Id), CancellationToken.None);

        result.Id.Should().Be(space.Id);
        result.Name.Should().Be("Project Alpha");
        result.Permission.Should().Be(held);
        result.Version.Should().Be(1);
        result.CreatedAt.Should().Be(TestSpaceFactory.Timestamp);
        result.UpdatedAt.Should().Be(TestSpaceFactory.Timestamp);
        result.Access.Should().BeEquivalentTo(
        [
            new SpaceAccessDto(TestSpaceFactory.OwnerId, SubjectType.User, SpacePermission.Owner, "Alice"),
            new SpaceAccessDto(TestSpaceFactory.WriterId, SubjectType.User, SpacePermission.Write, "Bob"),
            new SpaceAccessDto(TestSpaceFactory.ReaderId, SubjectType.User, SpacePermission.Read, "Carol"),
        ]);
    }

    [TestMethod]
    public async Task HandleAsksTheDirectoryForEveryMemberInOneRead()
    {
        Space space = TestSpaceFactory.CreateShared();
        IUserDirectoryRepository users = TestSpaceFactory.CreateDirectory();
        GetSpaceQueryHandler handler = new GetSpaceQueryHandler(
            TestSpaceFactory.CreateRepository(space),
            users,
            new TestCurrentUser(TestSpaceFactory.OwnerId));

        _ = await handler.Handle(new GetSpaceQuery(space.Id), CancellationToken.None);

        await users.Received(1).FindByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 3
                && ids.Contains(TestSpaceFactory.OwnerId)
                && ids.Contains(TestSpaceFactory.WriterId)
                && ids.Contains(TestSpaceFactory.ReaderId)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task HandleThrowsNotFoundWhenTheSpaceHasVanished()
    {
        GetSpaceQueryHandler handler = new GetSpaceQueryHandler(
            TestSpaceFactory.CreateRepository(),
            TestSpaceFactory.CreateDirectory(),
            new TestCurrentUser(TestSpaceFactory.OwnerId));

        Func<Task> act = () => handler.Handle(
            new GetSpaceQuery(TestSpaceFactory.SpaceId),
            CancellationToken.None);

        NotFoundException exception = (await act.Should().ThrowAsync<NotFoundException>()).Which;
        exception.ResourceName.Should().Be("Space");
        exception.ResourceId.Should().Be(TestSpaceFactory.SpaceId);
    }

    /// <summary>
    /// The access behavior admitted the caller from its own read of the Space.
    /// If an Owner removes them before the handler reads it again, the Space
    /// they are holding is one they no longer belong to, and the answer is the
    /// one any outsider gets rather than a failure.
    /// </summary>
    [TestMethod]
    public async Task HandleThrowsNotFoundWhenTheCallersAccessWasRevokedAfterTheCheck()
    {
        Space space = TestSpaceFactory.CreateShared();
        GetSpaceQueryHandler handler = new GetSpaceQueryHandler(
            TestSpaceFactory.CreateRepository(space),
            TestSpaceFactory.CreateDirectory((TestSpaceFactory.OwnerId, "Alice")),
            new TestCurrentUser(TestSpaceFactory.StrangerId));

        Func<Task> act = () => handler.Handle(
            new GetSpaceQuery(space.Id),
            CancellationToken.None);

        NotFoundException exception = (await act.Should().ThrowAsync<NotFoundException>()).Which;
        exception.ResourceName.Should().Be("Space");
        exception.ResourceId.Should().Be(space.Id);
    }

    private static Guid UserHolding(SpacePermission permission)
    {
        return permission switch
        {
            SpacePermission.Owner => TestSpaceFactory.OwnerId,
            SpacePermission.Write => TestSpaceFactory.WriterId,
            SpacePermission.Read => TestSpaceFactory.ReaderId,
            _ => throw new ArgumentOutOfRangeException(nameof(permission)),
        };
    }
}
