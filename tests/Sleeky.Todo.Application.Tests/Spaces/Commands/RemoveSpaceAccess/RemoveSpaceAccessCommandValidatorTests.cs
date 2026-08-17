using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Spaces.Commands.RemoveSpaceAccess;

namespace Sleeky.Todo.Application.Tests.Spaces.Commands.RemoveSpaceAccess;

[TestClass]
public sealed class RemoveSpaceAccessCommandValidatorTests
{
    private readonly RemoveSpaceAccessCommandValidator validator =
        new RemoveSpaceAccessCommandValidator();

    [TestMethod]
    public void ValidateAcceptsAWellFormedCommand()
    {
        ValidationResult result = validator.Validate(CreateCommand());

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void ValidateRejectsAnEmptySpaceIdentifier()
    {
        ValidationResult result = validator.Validate(CreateCommand(spaceId: Guid.Empty));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(RemoveSpaceAccessCommand.SpaceId));
    }

    [TestMethod]
    public void ValidateRejectsAnEmptySubjectIdentifier()
    {
        ValidationResult result = validator.Validate(CreateCommand(subjectId: Guid.Empty));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(RemoveSpaceAccessCommand.SubjectId));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void ValidateRejectsANonPositiveVersion(long version)
    {
        ValidationResult result = validator.Validate(CreateCommand(version: version));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(RemoveSpaceAccessCommand.Version));
    }

    private static RemoveSpaceAccessCommand CreateCommand(
        Guid? spaceId = null,
        Guid? subjectId = null,
        long version = 1)
    {
        return new RemoveSpaceAccessCommand(
            spaceId ?? TestSpaceFactory.SpaceId,
            subjectId ?? TestSpaceFactory.WriterId,
            version);
    }
}
