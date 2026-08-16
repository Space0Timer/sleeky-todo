using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Spaces.Commands.RemoveSpaceAccess;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Tests.Spaces.Commands.RemoveSpaceAccess;

[TestClass]
public sealed class RemoveSpaceAccessCommandHandlerTests
{
    private static readonly DateTimeOffset RevokedAt = TestSpaceFactory.Timestamp.AddHours(1);

    [TestMethod]
    public async Task HandleRevokesTheMemberAndReturnsTheSpaceOneVersionOn()
    {
        Space space = TestSpaceFactory.CreateShared();
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        RemoveSpaceAccessCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        SpaceDto result = await handler.Handle(
            new RemoveSpaceAccessCommand(space.Id, TestSpaceFactory.WriterId, 1),
            CancellationToken.None);

        result.Version.Should().Be(2);
        result.UpdatedAt.Should().Be(RevokedAt);
        result.Permission.Should().Be(SpacePermission.Owner);
        result.Access.Select(entry => entry.SubjectId).Should().BeEquivalentTo(
        [
            TestSpaceFactory.OwnerId,
            TestSpaceFactory.ReaderId,
        ]);
        await spaces.Received(1).UpdateAsync(
            Arg.Is<Space>(updated =>
                updated.PermissionFor(TestSpaceFactory.WriterId, SubjectType.User) == null),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Leaving a Space is not an operation this command offers, so a member
    /// naming themselves is refused before the Space is even read.
    /// </summary>
    [TestMethod]
    public async Task HandleRefusesToRevokeTheCallersOwnAccess()
    {
        Space space = TestSpaceFactory.CreateShared();
        space.AddAccess(TestSpaceFactory.StrangerId, SubjectType.User, SpacePermission.Owner, TestSpaceFactory.Timestamp);
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        RemoveSpaceAccessCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        Func<Task> act = () => handler.Handle(
            new RemoveSpaceAccessCommand(space.Id, TestSpaceFactory.OwnerId, 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        await spaces.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [TestMethod]
    public async Task HandleThrowsConcurrencyConflictBeforeWritingWhenTheVersionIsStale()
    {
        Space space = TestSpaceFactory.WithVersion(TestSpaceFactory.CreateShared(), 2);
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        RemoveSpaceAccessCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        Func<Task> act = () => handler.Handle(
            new RemoveSpaceAccessCommand(space.Id, TestSpaceFactory.WriterId, 1),
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
        RemoveSpaceAccessCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        Func<Task> act = () => handler.Handle(
            new RemoveSpaceAccessCommand(TestSpaceFactory.SpaceId, TestSpaceFactory.WriterId, 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    /// <summary>
    /// The entity refuses to remove a subject that has no access; the handler
    /// lets that rule through and writes nothing.
    /// </summary>
    [TestMethod]
    public async Task HandleLetsTheUnknownSubjectRuleThrough()
    {
        Space space = TestSpaceFactory.CreateShared();
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        RemoveSpaceAccessCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        Func<Task> act = () => handler.Handle(
            new RemoveSpaceAccessCommand(space.Id, TestSpaceFactory.StrangerId, 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    private static RemoveSpaceAccessCommandHandler CreateHandler(
        ISpaceRepository spaces,
        Guid userId)
    {
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(RevokedAt);

        return new RemoveSpaceAccessCommandHandler(
            spaces,
            TestSpaceFactory.CreateDirectory(),
            clock,
            new TestCurrentUser(userId),
            NullLogger<RemoveSpaceAccessCommandHandler>.Instance);
    }
}
