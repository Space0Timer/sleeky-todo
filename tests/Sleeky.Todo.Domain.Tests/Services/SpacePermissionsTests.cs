using FluentAssertions;

using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Services;

namespace Sleeky.Todo.Domain.Tests.Services;

[TestClass]
public sealed class SpacePermissionsTests
{
    [TestMethod]
    [DataRow(SpacePermission.Owner, SpacePermission.Read, true)]
    [DataRow(SpacePermission.Owner, SpacePermission.Write, true)]
    [DataRow(SpacePermission.Owner, SpacePermission.Owner, true)]
    [DataRow(SpacePermission.Write, SpacePermission.Read, true)]
    [DataRow(SpacePermission.Write, SpacePermission.Write, true)]
    [DataRow(SpacePermission.Write, SpacePermission.Owner, false)]
    [DataRow(SpacePermission.Read, SpacePermission.Read, true)]
    [DataRow(SpacePermission.Read, SpacePermission.Write, false)]
    [DataRow(SpacePermission.Read, SpacePermission.Owner, false)]
    public void EachLevelIncludesTheLevelsBelowIt(
        SpacePermission granted,
        SpacePermission required,
        bool expected)
    {
        SpacePermissions.Includes(granted, required).Should().Be(expected);
    }
}
