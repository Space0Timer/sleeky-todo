using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces;
using Sleeky.Todo.Application.Spaces.Commands.CreateSpace;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Spaces.Commands.CreateSpace;

[TestClass]
public sealed class CreateSpaceCommandHandlerTests
{
    [TestMethod]
    public async Task HandleCreatesPersistsAndReturnsTheSpaceWithTheCreatorAsOwner()
    {
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository();
        IUserDirectoryRepository users = TestSpaceFactory.CreateDirectory(
            (TestSpaceFactory.OwnerId, "Alice"));
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestSpaceFactory.Timestamp);
        CreateSpaceCommandHandler handler = new CreateSpaceCommandHandler(
            spaces,
            users,
            clock,
            new TestCurrentUser(TestSpaceFactory.OwnerId),
            NullLogger<CreateSpaceCommandHandler>.Instance);
        CancellationToken cancellationToken = new CancellationTokenSource().Token;

        SpaceDto result = await handler.Handle(
            new CreateSpaceCommand("  Project Alpha  "),
            cancellationToken);

        result.Id.Should().NotBe(Guid.Empty);
        result.Name.Should().Be("Project Alpha");
        result.Permission.Should().Be(SpacePermission.Owner);
        result.Version.Should().Be(1);
        result.CreatedAt.Should().Be(TestSpaceFactory.Timestamp);
        result.UpdatedAt.Should().Be(TestSpaceFactory.Timestamp);
        result.Access.Should().ContainSingle().Which.Should().Be(new SpaceAccessDto(
            TestSpaceFactory.OwnerId,
            SubjectType.User,
            SpacePermission.Owner,
            "Alice"));
        await spaces.Received(1).AddAsync(
            Arg.Is<Space>(space => space.Id == result.Id
                && space.PermissionFor(TestSpaceFactory.OwnerId, SubjectType.User) == SpacePermission.Owner),
            cancellationToken);
    }

    /// <summary>
    /// A created Space is a new one, never the user's personal Space: its
    /// identifier is minted rather than derived, so creating one named
    /// "My Space" does not collide with the personal Space of the same name.
    /// </summary>
    [TestMethod]
    public async Task HandleMintsAnIdentifierIndependentOfThePersonalSpace()
    {
        ISpaceRepository spaces = TestSpaceFactory.CreateRepository();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestSpaceFactory.Timestamp);
        CreateSpaceCommandHandler handler = new CreateSpaceCommandHandler(
            spaces,
            TestSpaceFactory.CreateDirectory(),
            clock,
            new TestCurrentUser(TestSpaceFactory.OwnerId),
            NullLogger<CreateSpaceCommandHandler>.Instance);

        SpaceDto first = await handler.Handle(
            new CreateSpaceCommand(PersonalSpace.Name),
            CancellationToken.None);
        SpaceDto second = await handler.Handle(
            new CreateSpaceCommand(PersonalSpace.Name),
            CancellationToken.None);

        first.Id.Should().NotBe(PersonalSpace.IdFor(TestSpaceFactory.OwnerId));
        second.Id.Should().NotBe(first.Id);
        await spaces.DidNotReceiveWithAnyArgs().GetOrAddAsync(default!, default);
    }

    /// <summary>
    /// A creator the directory has no name for is still the Owner; the entry
    /// simply carries no display name.
    /// </summary>
    [TestMethod]
    public async Task HandleLeavesTheDisplayNameNullWhenTheDirectoryDoesNotKnowTheCreator()
    {
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestSpaceFactory.Timestamp);
        CreateSpaceCommandHandler handler = new CreateSpaceCommandHandler(
            TestSpaceFactory.CreateRepository(),
            TestSpaceFactory.CreateDirectory(),
            clock,
            new TestCurrentUser(TestSpaceFactory.OwnerId),
            NullLogger<CreateSpaceCommandHandler>.Instance);

        SpaceDto result = await handler.Handle(
            new CreateSpaceCommand("Project Alpha"),
            CancellationToken.None);

        result.Access.Should().ContainSingle().Which.DisplayName.Should().BeNull();
    }
}
