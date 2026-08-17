using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces;
using Sleeky.Todo.Application.Spaces.Queries.GetSpaces;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Spaces.Queries.GetSpaces;

[TestClass]
public sealed class GetSpacesQueryHandlerTests
{
    private static readonly Guid UserId = TestSpaceFactory.WriterId;

    [TestMethod]
    public async Task HandleEnsuresThePersonalSpaceBeforeListing()
    {
        ISpaceRepository spaces = Substitute.For<ISpaceRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestSpaceFactory.Timestamp);
        List<string> order = new List<string>();
        spaces
            .GetOrAddAsync(Arg.Any<Space>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                order.Add("ensure");
                return call.Arg<Space>();
            });
        spaces
            .GetForSubjectAsync(UserId, SubjectType.User, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("list");
                return Array.Empty<Space>();
            });
        GetSpacesQueryHandler handler = new GetSpacesQueryHandler(
            spaces,
            clock,
            new TestCurrentUser(UserId));

        _ = await handler.Handle(new GetSpacesQuery(), CancellationToken.None);

        order.Should().Equal("ensure", "list");
        await spaces.Received(1).GetOrAddAsync(
            Arg.Is<Space>(space => space.Id == PersonalSpace.IdFor(UserId)
                && space.Name == PersonalSpace.Name
                && space.CreatedAt == TestSpaceFactory.Timestamp
                && space.PermissionFor(UserId, SubjectType.User) == SpacePermission.Owner),
            Arg.Any<CancellationToken>());
        await spaces.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [TestMethod]
    public async Task HandleReturnsEachMembershipWithTheCallersOwnLevelInRepositoryOrder()
    {
        Space personal = Space.Create(PersonalSpace.IdFor(UserId), PersonalSpace.Name, UserId, TestSpaceFactory.Timestamp);
        Space shared = TestSpaceFactory.CreateShared();
        Space readOnly = Space.Create(Guid.NewGuid(), "Marketing", TestSpaceFactory.OwnerId, TestSpaceFactory.Timestamp);
        readOnly.AddAccess(UserId, SubjectType.User, SpacePermission.Read, TestSpaceFactory.Timestamp);
        ISpaceRepository spaces = Substitute.For<ISpaceRepository>();
        spaces
            .GetOrAddAsync(Arg.Any<Space>(), Arg.Any<CancellationToken>())
            .Returns(personal);
        spaces
            .GetForSubjectAsync(UserId, SubjectType.User, Arg.Any<CancellationToken>())
            .Returns(new[] { personal, shared, readOnly });
        GetSpacesQueryHandler handler = new GetSpacesQueryHandler(
            spaces,
            Substitute.For<IClock>(),
            new TestCurrentUser(UserId));

        IReadOnlyList<SpaceSummaryDto> result = await handler.Handle(
            new GetSpacesQuery(),
            CancellationToken.None);

        result.Should().Equal(
            new SpaceSummaryDto(personal.Id, PersonalSpace.Name, SpacePermission.Owner),
            new SpaceSummaryDto(shared.Id, "Project Alpha", SpacePermission.Write),
            new SpaceSummaryDto(readOnly.Id, "Marketing", SpacePermission.Read));
    }

    /// <summary>
    /// The listing is what the repository says the user belongs to; a Space
    /// the personal-Space insert returned but the membership read did not is
    /// not invented into the result.
    /// </summary>
    [TestMethod]
    public async Task HandleListsOnlyWhatTheRepositoryReportsAsMemberships()
    {
        Space personal = Space.Create(PersonalSpace.IdFor(UserId), PersonalSpace.Name, UserId, TestSpaceFactory.Timestamp);
        ISpaceRepository spaces = Substitute.For<ISpaceRepository>();
        spaces
            .GetOrAddAsync(Arg.Any<Space>(), Arg.Any<CancellationToken>())
            .Returns(personal);
        spaces
            .GetForSubjectAsync(UserId, SubjectType.User, Arg.Any<CancellationToken>())
            .Returns(new[] { personal });
        GetSpacesQueryHandler handler = new GetSpacesQueryHandler(
            spaces,
            Substitute.For<IClock>(),
            new TestCurrentUser(UserId));

        IReadOnlyList<SpaceSummaryDto> result = await handler.Handle(
            new GetSpacesQuery(),
            CancellationToken.None);

        result.Should().ContainSingle().Which.Id.Should().Be(PersonalSpace.IdFor(UserId));
    }
}
