using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Spaces.Commands.AddSpaceAccess;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Spaces.Commands.AddSpaceAccess;

[TestClass]
public sealed class AddSpaceAccessCommandValidatorTests
{
    private readonly AddSpaceAccessCommandValidator validator = new AddSpaceAccessCommandValidator();

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
            failure => failure.PropertyName == nameof(AddSpaceAccessCommand.SpaceId));
    }

    [TestMethod]
    public void ValidateRejectsAnEmptySubjectIdentifier()
    {
        ValidationResult result = validator.Validate(CreateCommand(subjectId: Guid.Empty));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(AddSpaceAccessCommand.SubjectId));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(999)]
    public void ValidateRejectsAnUndefinedPermission(int permission)
    {
        ValidationResult result = validator.Validate(
            CreateCommand(permission: (SpacePermission)permission));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(AddSpaceAccessCommand.Permission));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void ValidateRejectsANonPositiveVersion(long version)
    {
        ValidationResult result = validator.Validate(CreateCommand(version: version));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(AddSpaceAccessCommand.Version));
    }

    private static AddSpaceAccessCommand CreateCommand(
        Guid? spaceId = null,
        Guid? subjectId = null,
        SpacePermission permission = SpacePermission.Read,
        long version = 1)
    {
        return new AddSpaceAccessCommand(
            spaceId ?? TestSpaceFactory.SpaceId,
            subjectId ?? TestSpaceFactory.StrangerId,
            permission,
            version);
    }
}
