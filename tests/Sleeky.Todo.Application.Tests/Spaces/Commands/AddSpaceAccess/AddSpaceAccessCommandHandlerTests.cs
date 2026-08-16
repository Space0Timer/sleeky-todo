using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Spaces.Commands.AddSpaceAccess;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Tests.Spaces.Commands.AddSpaceAccess;

[TestClass]
public sealed class AddSpaceAccessCommandHandlerTests
{
    private static readonly DateTimeOffset GrantedAt = TestSpaceFactory.Timestamp.AddHours(1);

    [TestMethod]
    [DataRow(SpacePermission.Read)]
    [DataRow(SpacePermission.Write)]
    [DataRow(SpacePermission.Owner)]
    public async Task HandleGrantsAKnownUserAccessAndReturnsTheSpaceOneVersionOn(
        SpacePermission permission)
    {
        Space space = TestSpaceFactory.CreateOwned();
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        IUserDirectoryRepository users = TestSpaceFactory.CreateDirectory(
            (TestSpaceFactory.OwnerId, "Alice"),
            (TestSpaceFactory.StrangerId, "Dave"));
        AddSpaceAccessCommandHandler handler = CreateHandler(spaces, users);

        SpaceDto result = await handler.Handle(
            new AddSpaceAccessCommand(space.Id, TestSpaceFactory.StrangerId, permission, 1),
            CancellationToken.None);

        result.Version.Should().Be(2);
        result.UpdatedAt.Should().Be(GrantedAt);
        result.Permission.Should().Be(SpacePermission.Owner);
        result.Access.Should().BeEquivalentTo(
        [
            new SpaceAccessDto(TestSpaceFactory.OwnerId, SubjectType.User, SpacePermission.Owner, "Alice"),
            new SpaceAccessDto(TestSpaceFactory.StrangerId, SubjectType.User, permission, "Dave"),
        ]);
        await spaces.Received(1).UpdateAsync(
            Arg.Is<Space>(updated =>
                updated.PermissionFor(TestSpaceFactory.StrangerId, SubjectType.User) == permission),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task HandleThrowsNotFoundForAUserTheDirectoryDoesNotKnow()
    {
        Space space = TestSpaceFactory.CreateOwned();
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        AddSpaceAccessCommandHandler handler = CreateHandler(
            spaces,
            TestSpaceFactory.CreateDirectory((TestSpaceFactory.OwnerId, "Alice")));

        Func<Task> act = () => handler.Handle(
            new AddSpaceAccessCommand(space.Id, TestSpaceFactory.StrangerId, SpacePermission.Read, 1),
            CancellationToken.None);

        NotFoundException exception = (await act.Should().ThrowAsync<NotFoundException>()).Which;
        exception.ResourceName.Should().Be("User");
        exception.ResourceId.Should().Be(TestSpaceFactory.StrangerId);
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [TestMethod]
    public async Task HandleThrowsConcurrencyConflictBeforeWritingWhenTheVersionIsStale()
    {
        Space space = TestSpaceFactory.WithVersion(TestSpaceFactory.CreateOwned(), 2);
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        IUserDirectoryRepository users = TestSpaceFactory.CreateDirectory(
            (TestSpaceFactory.StrangerId, "Dave"));
        AddSpaceAccessCommandHandler handler = CreateHandler(spaces, users);

        Func<Task> act = () => handler.Handle(
            new AddSpaceAccessCommand(space.Id, TestSpaceFactory.StrangerId, SpacePermission.Read, 1),
            CancellationToken.None);

        ConcurrencyConflictException exception =
            (await act.Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        exception.ExpectedVersion.Should().Be(1);
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [TestMethod]
    public async Task HandleThrowsNotFoundWhenTheSpaceHasVanished()
    {
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository();
        AddSpaceAccessCommandHandler handler = CreateHandler(
            spaces,
            TestSpaceFactory.CreateDirectory((TestSpaceFactory.StrangerId, "Dave")));

        Func<Task> act = () => handler.Handle(
            new AddSpaceAccessCommand(
                TestSpaceFactory.SpaceId,
                TestSpaceFactory.StrangerId,
                SpacePermission.Read,
                1),
            CancellationToken.None);

        NotFoundException exception = (await act.Should().ThrowAsync<NotFoundException>()).Which;
        exception.ResourceName.Should().Be("Space");
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    /// <summary>
    /// The entity refuses a second entry for the same subject; the handler
    /// lets that rule through unchanged, and nothing is written.
    /// </summary>
    [TestMethod]
    public async Task HandleLetsTheDuplicateSubjectRuleThrough()
    {
        Space space = TestSpaceFactory.CreateShared();
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        AddSpaceAccessCommandHandler handler = CreateHandler(
            spaces,
            TestSpaceFactory.CreateDirectory((TestSpaceFactory.WriterId, "Bob")));

        Func<Task> act = () => handler.Handle(
            new AddSpaceAccessCommand(space.Id, TestSpaceFactory.WriterId, SpacePermission.Read, 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    private static AddSpaceAccessCommandHandler CreateHandler(
        ISpaceRepository spaces,
        IUserDirectoryRepository users)
    {
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(GrantedAt);

        return new AddSpaceAccessCommandHandler(
            spaces,
            users,
            clock,
            new TestCurrentUser(TestSpaceFactory.OwnerId),
            NullLogger<AddSpaceAccessCommandHandler>.Instance);
    }
}
