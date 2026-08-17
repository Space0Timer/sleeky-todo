using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Spaces.Commands.ChangeSpacePermission;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Tests.Spaces.Commands.ChangeSpacePermission;

[TestClass]
public sealed class ChangeSpacePermissionCommandHandlerTests
{
    private static readonly DateTimeOffset ChangedAt = TestSpaceFactory.Timestamp.AddHours(1);

    [TestMethod]
    public async Task HandleMovesTheMemberToTheNewLevelAndReturnsTheSpaceOneVersionOn()
    {
        Space space = TestSpaceFactory.CreateShared();
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        ChangeSpacePermissionCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        SpaceDto result = await handler.Handle(
            new ChangeSpacePermissionCommand(
                space.Id,
                TestSpaceFactory.ReaderId,
                SpacePermission.Write,
                1),
            CancellationToken.None);

        result.Version.Should().Be(2);
        result.UpdatedAt.Should().Be(ChangedAt);
        result.Permission.Should().Be(SpacePermission.Owner);
        result.Access.Should().ContainSingle(entry => entry.SubjectId == TestSpaceFactory.ReaderId)
            .Which.Permission.Should().Be(SpacePermission.Write);
        await spaces.Received(1).UpdateAsync(
            Arg.Is<Space>(updated =>
                updated.PermissionFor(TestSpaceFactory.ReaderId, SubjectType.User) == SpacePermission.Write),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An Owner may step down while another Owner remains, and the response
    /// then reports the caller's new, lower level.
    /// </summary>
    [TestMethod]
    public async Task HandleReportsTheCallersOwnLevelAfterTheyStepDown()
    {
        Space space = TestSpaceFactory.CreateOwned();
        space.AddAccess(TestSpaceFactory.WriterId, SubjectType.User, SpacePermission.Owner, TestSpaceFactory.Timestamp);
        ChangeSpacePermissionCommandHandler handler = CreateHandler(
            TestSpaceFactory.CreateRepository(space),
            TestSpaceFactory.OwnerId);

        SpaceDto result = await handler.Handle(
            new ChangeSpacePermissionCommand(
                space.Id,
                TestSpaceFactory.OwnerId,
                SpacePermission.Read,
                1),
            CancellationToken.None);

        result.Permission.Should().Be(SpacePermission.Read);
    }

    [TestMethod]
    public async Task HandleThrowsConcurrencyConflictBeforeWritingWhenTheVersionIsStale()
    {
        Space space = TestSpaceFactory.WithVersion(TestSpaceFactory.CreateShared(), 4);
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        ChangeSpacePermissionCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        Func<Task> act = () => handler.Handle(
            new ChangeSpacePermissionCommand(
                space.Id,
                TestSpaceFactory.ReaderId,
                SpacePermission.Write,
                3),
            CancellationToken.None);

        ConcurrencyConflictException exception =
            (await act.Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        exception.ExpectedVersion.Should().Be(3);
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [TestMethod]
    public async Task HandleThrowsNotFoundWhenTheSpaceHasVanished()
    {
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository();
        ChangeSpacePermissionCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        Func<Task> act = () => handler.Handle(
            new ChangeSpacePermissionCommand(
                TestSpaceFactory.SpaceId,
                TestSpaceFactory.ReaderId,
                SpacePermission.Write,
                1),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    /// <summary>
    /// The entity refuses to downgrade the last Owner and to change a subject
    /// that has no access; the handler lets both rules through and writes
    /// nothing.
    /// </summary>
    [TestMethod]
    public async Task HandleLetsTheEntityRulesThrough()
    {
        Space space = TestSpaceFactory.CreateShared();
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        ChangeSpacePermissionCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        Func<Task> downgradeLastOwner = () => handler.Handle(
            new ChangeSpacePermissionCommand(space.Id, TestSpaceFactory.OwnerId, SpacePermission.Write, 1),
            CancellationToken.None);
        Func<Task> changeStranger = () => handler.Handle(
            new ChangeSpacePermissionCommand(space.Id, TestSpaceFactory.StrangerId, SpacePermission.Write, 1),
            CancellationToken.None);

        await downgradeLastOwner.Should().ThrowAsync<DomainException>();
        await changeStranger.Should().ThrowAsync<DomainException>();
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    private static ChangeSpacePermissionCommandHandler CreateHandler(
        ISpaceRepository spaces,
        Guid userId)
    {
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(ChangedAt);

        return new ChangeSpacePermissionCommandHandler(
            spaces,
            TestSpaceFactory.CreateDirectory(),
            clock,
            new TestCurrentUser(userId),
            NullLogger<ChangeSpacePermissionCommandHandler>.Instance);
    }
}
