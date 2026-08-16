using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Spaces.Commands.RenameSpace;
using Sleeky.Todo.Application.Spaces.Validation;

namespace Sleeky.Todo.Application.Tests.Spaces.Commands.RenameSpace;

[TestClass]
public sealed class RenameSpaceCommandValidatorTests
{
    private readonly RenameSpaceCommandValidator validator = new RenameSpaceCommandValidator();

    [TestMethod]
    public void ValidateAcceptsAWellFormedCommand()
    {
        RenameSpaceCommand command = new RenameSpaceCommand(
            TestSpaceFactory.SpaceId,
            $"  {new string('n', SpaceValidationLimits.NameMaximumLength)}  ",
            1);

        ValidationResult result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void ValidateRejectsAnEmptySpaceIdentifier()
    {
        RenameSpaceCommand command = new RenameSpaceCommand(Guid.Empty, "Project Beta", 1);

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(RenameSpaceCommand.SpaceId));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ValidateRejectsABlankName(string name)
    {
        RenameSpaceCommand command = new RenameSpaceCommand(TestSpaceFactory.SpaceId, name, 1);

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(RenameSpaceCommand.Name));
    }

    [TestMethod]
    public void ValidateRejectsANameOverTheMaximumTrimmedLength()
    {
        RenameSpaceCommand command = new RenameSpaceCommand(
            TestSpaceFactory.SpaceId,
            new string('n', SpaceValidationLimits.NameMaximumLength + 1),
            1);

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(RenameSpaceCommand.Name));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void ValidateRejectsANonPositiveVersion(long version)
    {
        RenameSpaceCommand command = new RenameSpaceCommand(
            TestSpaceFactory.SpaceId,
            "Project Beta",
            version);

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(RenameSpaceCommand.Version));
    }
}
