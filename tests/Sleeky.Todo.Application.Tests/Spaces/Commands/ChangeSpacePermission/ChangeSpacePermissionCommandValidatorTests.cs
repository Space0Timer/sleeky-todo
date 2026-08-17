using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Spaces.Commands.ChangeSpacePermission;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Spaces.Commands.ChangeSpacePermission;

[TestClass]
public sealed class ChangeSpacePermissionCommandValidatorTests
{
    private readonly ChangeSpacePermissionCommandValidator validator =
        new ChangeSpacePermissionCommandValidator();

    [TestMethod]
    [DataRow(SpacePermission.Read)]
    [DataRow(SpacePermission.Write)]
    [DataRow(SpacePermission.Owner)]
    public void ValidateAcceptsEveryDefinedPermission(SpacePermission permission)
    {
        ValidationResult result = validator.Validate(CreateCommand(permission: permission));

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void ValidateRejectsAnEmptySpaceIdentifier()
    {
        ValidationResult result = validator.Validate(CreateCommand(spaceId: Guid.Empty));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(ChangeSpacePermissionCommand.SpaceId));
    }

    [TestMethod]
    public void ValidateRejectsAnEmptySubjectIdentifier()
    {
        ValidationResult result = validator.Validate(CreateCommand(subjectId: Guid.Empty));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(ChangeSpacePermissionCommand.SubjectId));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    public void ValidateRejectsAnUndefinedPermission(int permission)
    {
        ValidationResult result = validator.Validate(
            CreateCommand(permission: (SpacePermission)permission));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(ChangeSpacePermissionCommand.Permission));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void ValidateRejectsANonPositiveVersion(long version)
    {
        ValidationResult result = validator.Validate(CreateCommand(version: version));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(ChangeSpacePermissionCommand.Version));
    }

    private static ChangeSpacePermissionCommand CreateCommand(
        Guid? spaceId = null,
        Guid? subjectId = null,
        SpacePermission permission = SpacePermission.Write,
        long version = 1)
    {
        return new ChangeSpacePermissionCommand(
            spaceId ?? TestSpaceFactory.SpaceId,
            subjectId ?? TestSpaceFactory.ReaderId,
            permission,
            version);
    }
}
