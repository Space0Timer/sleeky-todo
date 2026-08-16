using FluentAssertions;

using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Tests.Entities;

[TestClass]
public sealed class SpaceTests
{
    private static readonly DateTimeOffset InitialTimestamp = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset LaterTimestamp = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.FromHours(8));
    private static readonly Guid SpaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OwnerUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MemberUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [TestMethod]
    public void CreateMakesTheCreatorTheOnlyOwner()
    {
        Space space = Space.Create(SpaceId, "  Project Alpha  ", OwnerUserId, InitialTimestamp);

        space.Id.Should().Be(SpaceId);
        space.Name.Should().Be("Project Alpha");
        space.Access.Should().ContainSingle().Which.Should().Be(
            new SpaceAccessEntry(OwnerUserId, SubjectType.User, SpacePermission.Owner));
        space.Version.Should().Be(1);
        space.CreatedAt.Should().Be(InitialTimestamp.ToUniversalTime());
        space.UpdatedAt.Should().Be(InitialTimestamp.ToUniversalTime());
    }

    [TestMethod]
    public void CreateRejectsMissingIdentifier()
    {
        Func<Space> act = () => Space.Create(Guid.Empty, "Project Alpha", OwnerUserId, InitialTimestamp);

        act.Should().Throw<DomainException>().WithMessage("A Space identifier is required.");
    }

    [TestMethod]
    public void CreateRejectsBlankName()
    {
        Func<Space> act = () => Space.Create(SpaceId, "   ", OwnerUserId, InitialTimestamp);

        act.Should().Throw<DomainException>().WithMessage("A Space name is required.");
    }

    [TestMethod]
    public void CreateRejectsMissingOwner()
    {
        Func<Space> act = () => Space.Create(SpaceId, "Project Alpha", Guid.Empty, InitialTimestamp);

        act.Should().Throw<DomainException>()
            .WithMessage("A Space access subject identifier is required.");
    }

    [TestMethod]
    public void RenameTrimsAndTouches()
    {
        Space space = CreateSpace();

        space.Rename("  Marketing  ", LaterTimestamp);

        space.Name.Should().Be("Marketing");
        space.UpdatedAt.Should().Be(LaterTimestamp.ToUniversalTime());
        space.Version.Should().Be(1, "the repository advances the version, not the entity");
    }

    [TestMethod]
    public void RenameRejectsBlankName()
    {
        Space space = CreateSpace();

        Action act = () => space.Rename(string.Empty, LaterTimestamp);

        act.Should().Throw<DomainException>().WithMessage("A Space name is required.");
    }

    [TestMethod]
    public void AddAccessGrantsTheSubject()
    {
        Space space = CreateSpace();

        space.AddAccess(MemberUserId, SubjectType.User, SpacePermission.Write, LaterTimestamp);

        space.PermissionFor(MemberUserId, SubjectType.User).Should().Be(SpacePermission.Write);
        space.Access.Should().HaveCount(2);
        space.UpdatedAt.Should().Be(LaterTimestamp.ToUniversalTime());
    }

    [TestMethod]
    public void AddAccessRejectsADuplicateSubject()
    {
        Space space = CreateSpace();
        space.AddAccess(MemberUserId, SubjectType.User, SpacePermission.Read, LaterTimestamp);

        Action act = () =>
            space.AddAccess(MemberUserId, SubjectType.User, SpacePermission.Write, LaterTimestamp);

        act.Should().Throw<DomainException>()
            .WithMessage("The subject already has access to the Space.");
        space.PermissionFor(MemberUserId, SubjectType.User).Should().Be(SpacePermission.Read);
    }

    [TestMethod]
    public void AddAccessRejectsInvalidValues()
    {
        Space space = CreateSpace();

        Action missingSubject = () =>
            space.AddAccess(Guid.Empty, SubjectType.User, SpacePermission.Read, LaterTimestamp);
        Action undefinedType = () =>
            space.AddAccess(MemberUserId, (SubjectType)99, SpacePermission.Read, LaterTimestamp);
        Action undefinedPermission = () =>
            space.AddAccess(MemberUserId, SubjectType.User, (SpacePermission)99, LaterTimestamp);

        missingSubject.Should().Throw<DomainException>()
            .WithMessage("A Space access subject identifier is required.");
        undefinedType.Should().Throw<DomainException>()
            .WithMessage("A valid Space access subject type is required.");
        undefinedPermission.Should().Throw<DomainException>()
            .WithMessage("A valid Space permission is required.");
        space.Access.Should().ContainSingle();
    }

    [TestMethod]
    public void ChangePermissionMovesTheSubject()
    {
        Space space = CreateSpace();
        space.AddAccess(MemberUserId, SubjectType.User, SpacePermission.Read, InitialTimestamp);

        space.ChangePermission(MemberUserId, SubjectType.User, SpacePermission.Owner, LaterTimestamp);

        space.PermissionFor(MemberUserId, SubjectType.User).Should().Be(SpacePermission.Owner);
        space.PermissionFor(OwnerUserId, SubjectType.User).Should().Be(SpacePermission.Owner);
        space.UpdatedAt.Should().Be(LaterTimestamp.ToUniversalTime());
    }

    [TestMethod]
    public void ChangePermissionRejectsAnUnknownSubject()
    {
        Space space = CreateSpace();

        Action act = () =>
            space.ChangePermission(OtherUserId, SubjectType.User, SpacePermission.Read, LaterTimestamp);

        act.Should().Throw<DomainException>()
            .WithMessage("The subject has no access to the Space.");
    }

    [TestMethod]
    public void ChangePermissionRefusesToDowngradeTheLastOwner()
    {
        Space space = CreateSpace();
        space.AddAccess(MemberUserId, SubjectType.User, SpacePermission.Write, InitialTimestamp);

        Action act = () =>
            space.ChangePermission(OwnerUserId, SubjectType.User, SpacePermission.Write, LaterTimestamp);

        act.Should().Throw<DomainException>()
            .WithMessage("A Space must keep at least one Owner.");
        space.PermissionFor(OwnerUserId, SubjectType.User).Should().Be(SpacePermission.Owner);
        space.UpdatedAt.Should().Be(InitialTimestamp.ToUniversalTime());
    }

    [TestMethod]
    public void ChangePermissionDowngradesAnOwnerWhenAnotherRemains()
    {
        Space space = CreateSpace();
        space.AddAccess(MemberUserId, SubjectType.User, SpacePermission.Owner, InitialTimestamp);

        space.ChangePermission(OwnerUserId, SubjectType.User, SpacePermission.Read, LaterTimestamp);

        space.PermissionFor(OwnerUserId, SubjectType.User).Should().Be(SpacePermission.Read);
    }

    [TestMethod]
    public void RemoveAccessRevokesTheSubject()
    {
        Space space = CreateSpace();
        space.AddAccess(MemberUserId, SubjectType.User, SpacePermission.Write, InitialTimestamp);

        space.RemoveAccess(MemberUserId, SubjectType.User, LaterTimestamp);

        space.PermissionFor(MemberUserId, SubjectType.User).Should().BeNull();
        space.Access.Should().ContainSingle();
        space.UpdatedAt.Should().Be(LaterTimestamp.ToUniversalTime());
    }

    [TestMethod]
    public void RemoveAccessRejectsAnUnknownSubject()
    {
        Space space = CreateSpace();

        Action act = () => space.RemoveAccess(OtherUserId, SubjectType.User, LaterTimestamp);

        act.Should().Throw<DomainException>()
            .WithMessage("The subject has no access to the Space.");
    }

    [TestMethod]
    public void RemoveAccessRefusesToRemoveTheLastOwner()
    {
        Space space = CreateSpace();
        space.AddAccess(MemberUserId, SubjectType.User, SpacePermission.Write, InitialTimestamp);

        Action act = () => space.RemoveAccess(OwnerUserId, SubjectType.User, LaterTimestamp);

        act.Should().Throw<DomainException>()
            .WithMessage("A Space must keep at least one Owner.");
        space.Access.Should().HaveCount(2);
    }

    [TestMethod]
    public void RemoveAccessRemovesAnOwnerWhenAnotherRemains()
    {
        Space space = CreateSpace();
        space.AddAccess(MemberUserId, SubjectType.User, SpacePermission.Owner, InitialTimestamp);

        space.RemoveAccess(OwnerUserId, SubjectType.User, LaterTimestamp);

        space.PermissionFor(OwnerUserId, SubjectType.User).Should().BeNull();
        space.PermissionFor(MemberUserId, SubjectType.User).Should().Be(SpacePermission.Owner);
    }

    [TestMethod]
    public void PermissionForIsNullForANonMember()
    {
        Space space = CreateSpace();

        space.PermissionFor(OtherUserId, SubjectType.User).Should().BeNull();
    }

    [TestMethod]
    public void RehydrateKeepsStoredState()
    {
        SpaceAccessEntry[] access =
        [
            new SpaceAccessEntry(OwnerUserId, SubjectType.User, SpacePermission.Owner),
            new SpaceAccessEntry(MemberUserId, SubjectType.User, SpacePermission.Read),
        ];

        Space space = Space.Rehydrate(SpaceId, "Project Alpha", access, 7, InitialTimestamp, LaterTimestamp);

        space.Id.Should().Be(SpaceId);
        space.Name.Should().Be("Project Alpha");
        space.Access.Should().Equal(access);
        space.Version.Should().Be(7);
        space.CreatedAt.Should().Be(InitialTimestamp.ToUniversalTime());
        space.UpdatedAt.Should().Be(LaterTimestamp.ToUniversalTime());
    }

    [TestMethod]
    public void RehydrateRejectsANonPositiveVersion()
    {
        Func<Space> act = () => Space.Rehydrate(
            SpaceId,
            "Project Alpha",
            [new SpaceAccessEntry(OwnerUserId, SubjectType.User, SpacePermission.Owner)],
            0,
            InitialTimestamp,
            InitialTimestamp);

        act.Should().Throw<DomainException>().WithMessage("A positive Space version is required.");
    }

    [TestMethod]
    public void RehydrateRejectsAnAccessListWithoutAnOwner()
    {
        Func<Space> act = () => Space.Rehydrate(
            SpaceId,
            "Project Alpha",
            [new SpaceAccessEntry(MemberUserId, SubjectType.User, SpacePermission.Write)],
            1,
            InitialTimestamp,
            InitialTimestamp);

        act.Should().Throw<DomainException>().WithMessage("A Space must have at least one Owner.");
    }

    [TestMethod]
    public void RehydrateRejectsADuplicateSubject()
    {
        Func<Space> act = () => Space.Rehydrate(
            SpaceId,
            "Project Alpha",
            [
                new SpaceAccessEntry(OwnerUserId, SubjectType.User, SpacePermission.Owner),
                new SpaceAccessEntry(OwnerUserId, SubjectType.User, SpacePermission.Read),
            ],
            1,
            InitialTimestamp,
            InitialTimestamp);

        act.Should().Throw<DomainException>()
            .WithMessage("The subject already has access to the Space.");
    }

    private static Space CreateSpace()
    {
        return Space.Create(SpaceId, "Project Alpha", OwnerUserId, InitialTimestamp);
    }
}
