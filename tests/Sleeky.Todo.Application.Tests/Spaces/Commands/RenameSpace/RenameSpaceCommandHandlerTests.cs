using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Spaces.Commands.RenameSpace;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Spaces.Commands.RenameSpace;

[TestClass]
public sealed class RenameSpaceCommandHandlerTests
{
    private static readonly DateTimeOffset RenamedAt = TestSpaceFactory.Timestamp.AddHours(1);

    [TestMethod]
    public async Task HandleRenamesTheSpaceAndReturnsItOneVersionOn()
    {
        Space space = TestSpaceFactory.CreateShared();
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        RenameSpaceCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        SpaceDto result = await handler.Handle(
            new RenameSpaceCommand(space.Id, "  Project Beta  ", 1),
            CancellationToken.None);

        result.Name.Should().Be("Project Beta");
        result.Version.Should().Be(2);
        result.UpdatedAt.Should().Be(RenamedAt);
        result.Permission.Should().Be(SpacePermission.Owner);
        result.Access.Should().HaveCount(3);
        await spaces.Received(1).UpdateAsync(
            Arg.Is<Space>(updated => updated.Id == space.Id && updated.Name == "Project Beta"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task HandleResolvesDisplayNamesForTheAccessList()
    {
        Space space = TestSpaceFactory.CreateShared();
        IUserDirectoryRepository users = TestSpaceFactory.CreateDirectory(
            (TestSpaceFactory.OwnerId, "Alice"),
            (TestSpaceFactory.WriterId, "Bob"));
        RenameSpaceCommandHandler handler = CreateHandler(
            TestSpaceFactory.CreateRepository(space),
            TestSpaceFactory.OwnerId,
            users);

        SpaceDto result = await handler.Handle(
            new RenameSpaceCommand(space.Id, "Project Beta", 1),
            CancellationToken.None);

        result.Access.Should().BeEquivalentTo(
        [
            new SpaceAccessDto(TestSpaceFactory.OwnerId, SubjectType.User, SpacePermission.Owner, "Alice"),
            new SpaceAccessDto(TestSpaceFactory.WriterId, SubjectType.User, SpacePermission.Write, "Bob"),
            new SpaceAccessDto(TestSpaceFactory.ReaderId, SubjectType.User, SpacePermission.Read, null),
        ]);
    }

    [TestMethod]
    public async Task HandleThrowsNotFoundWhenTheSpaceHasVanished()
    {
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository();
        RenameSpaceCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        Func<Task> act = () => handler.Handle(
            new RenameSpaceCommand(TestSpaceFactory.SpaceId, "Project Beta", 1),
            CancellationToken.None);

        NotFoundException exception = (await act.Should().ThrowAsync<NotFoundException>()).Which;
        exception.ResourceName.Should().Be("Space");
        exception.ResourceId.Should().Be(TestSpaceFactory.SpaceId);
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [TestMethod]
    public async Task HandleThrowsConcurrencyConflictBeforeWritingWhenTheVersionIsStale()
    {
        Space space = TestSpaceFactory.WithVersion(TestSpaceFactory.CreateShared(), 3);
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        RenameSpaceCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        Func<Task> act = () => handler.Handle(
            new RenameSpaceCommand(space.Id, "Project Beta", 2),
            CancellationToken.None);

        ConcurrencyConflictException exception =
            (await act.Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        exception.ResourceName.Should().Be("Space");
        exception.ExpectedVersion.Should().Be(2);
        await spaces.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [TestMethod]
    public async Task HandlePropagatesTheConflictWhenTheVersionedWriteLosesTheRace()
    {
        Space space = TestSpaceFactory.CreateShared();
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository(space);
        spaces
            .UpdateAsync(Arg.Any<Space>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("Space", space.Id, 1));
        RenameSpaceCommandHandler handler = CreateHandler(spaces, TestSpaceFactory.OwnerId);

        Func<Task> act = () => handler.Handle(
            new RenameSpaceCommand(space.Id, "Project Beta", 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    private static RenameSpaceCommandHandler CreateHandler(
        ISpaceRepository spaces,
        Guid userId,
        IUserDirectoryRepository? users = null)
    {
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(RenamedAt);

        return new RenameSpaceCommandHandler(
            spaces,
            users ?? TestSpaceFactory.CreateDirectory(),
            clock,
            new TestCurrentUser(userId),
            NullLogger<RenameSpaceCommandHandler>.Instance);
    }
}
