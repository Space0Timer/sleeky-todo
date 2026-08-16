using FluentAssertions;

using Sleeky.Todo.Application.Spaces;

namespace Sleeky.Todo.Application.Tests.Spaces;

[TestClass]
public sealed class PersonalSpaceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    /// <summary>
    /// The identifier is a pure function of the user, which is what lets two
    /// simultaneous first requests insert the same document instead of racing
    /// to create two.
    /// </summary>
    [TestMethod]
    public void IdForIsDeterministicPerUser()
    {
        Guid first = PersonalSpace.IdFor(UserId);
        Guid second = PersonalSpace.IdFor(UserId);

        second.Should().Be(first);
        first.Should().NotBe(Guid.Empty);
        first.Should().NotBe(UserId, "the Space is not the user");
    }

    [TestMethod]
    public void IdForDiffersBetweenUsers()
    {
        PersonalSpace.IdFor(UserId).Should().NotBe(PersonalSpace.IdFor(OtherUserId));
    }

    /// <summary>
    /// A name-based UUID is recognisable as one: version nibble 5 and the
    /// RFC 4122 variant, so the value is a well-formed identifier wherever it
    /// is later inspected, not merely sixteen hashed bytes.
    /// </summary>
    [TestMethod]
    public void IdForProducesAVersionFiveUuid()
    {
        Guid id = PersonalSpace.IdFor(UserId);
        string text = id.ToString("D");

        text[14].Should().Be('5');
        text[19].Should().BeOneOf('8', '9', 'a', 'b');
    }

    /// <summary>
    /// Pinned to the value an independent RFC 4122 implementation derives, so
    /// a change to the namespace or the hashing input is caught here rather
    /// than by every user quietly gaining a second personal Space on their
    /// next request.
    /// </summary>
    [TestMethod]
    public void IdForIsStableAcrossVersions()
    {
        PersonalSpace.IdFor(UserId).Should().Be(Guid.Parse("1f29fa82-99db-55c4-beef-9af216159ed0"));
    }

    [TestMethod]
    public void IdForRejectsAnEmptyUser()
    {
        Func<Guid> act = () => PersonalSpace.IdFor(Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("userId");
    }

    [TestMethod]
    public void TheNameIsMySpace()
    {
        PersonalSpace.Name.Should().Be("My Space");
    }
}
