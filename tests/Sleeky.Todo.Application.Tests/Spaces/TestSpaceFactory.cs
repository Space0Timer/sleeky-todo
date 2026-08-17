using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Spaces;

/// <summary>
/// The Spaces, members, and repository doubles the Space handler tests share.
/// </summary>
internal static class TestSpaceFactory
{
    public static readonly DateTimeOffset Timestamp = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
    public static readonly Guid SpaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid WriterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid ReaderId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid StrangerId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>
    /// A Space whose only member is <see cref="OwnerId"/>.
    /// </summary>
    public static Space CreateOwned(string name = "Project Alpha")
    {
        return Space.Create(SpaceId, name, OwnerId, Timestamp);
    }

    /// <summary>
    /// A Space with an Owner, a Write member, and a Read member.
    /// </summary>
    public static Space CreateShared()
    {
        Space space = CreateOwned();
        space.AddAccess(WriterId, SubjectType.User, SpacePermission.Write, Timestamp);
        space.AddAccess(ReaderId, SubjectType.User, SpacePermission.Read, Timestamp);

        return space;
    }

    /// <summary>
    /// The same Space at a different stored version, the way the repository
    /// hands it back after a versioned write.
    /// </summary>
    public static Space WithVersion(Space space, long version)
    {
        return Space.Rehydrate(
            space.Id,
            space.Name,
            space.Access,
            version,
            space.CreatedAt,
            space.UpdatedAt);
    }

    /// <summary>
    /// A repository that hands out <paramref name="stored"/> by identifier and
    /// answers every update with the written Space one version on, as the real
    /// one does.
    /// </summary>
    public static ISpaceRepository CreateRepository(params Space[] stored)
    {
        ISpaceRepository spaces = Substitute.For<ISpaceRepository>();
        foreach (Space space in stored)
        {
            spaces.GetByIdAsync(space.Id, Arg.Any<CancellationToken>()).Returns(space);
        }

        spaces
            .UpdateAsync(Arg.Any<Space>(), Arg.Any<CancellationToken>())
            .Returns(call => WithVersion(call.Arg<Space>(), call.Arg<Space>().Version + 1));

        return spaces;
    }

    /// <summary>
    /// A directory that knows the given users by name and no one else.
    /// </summary>
    public static IUserDirectoryRepository CreateDirectory(
        params (Guid UserId, string? DisplayName)[] knownUsers)
    {
        IUserDirectoryRepository users = Substitute.For<IUserDirectoryRepository>();
        users
            .FindByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => knownUsers
                .Where(user => call.Arg<IReadOnlyCollection<Guid>>().Contains(user.UserId))
                .Select(user => new UserIdentity(user.UserId, user.DisplayName))
                .ToArray());

        return users;
    }
}
