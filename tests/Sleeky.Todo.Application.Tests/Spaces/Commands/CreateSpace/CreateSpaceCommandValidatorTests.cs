using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Spaces.Commands.CreateSpace;
using Sleeky.Todo.Application.Spaces.Validation;

namespace Sleeky.Todo.Application.Tests.Spaces.Commands.CreateSpace;

[TestClass]
public sealed class CreateSpaceCommandValidatorTests
{
    private readonly CreateSpaceCommandValidator validator = new CreateSpaceCommandValidator();

    [TestMethod]
    public void ValidateAcceptsATrimmedBoundaryLengthName()
    {
        CreateSpaceCommand command = new CreateSpaceCommand(
            $"  {new string('n', SpaceValidationLimits.NameMaximumLength)}  ");

        ValidationResult result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ValidateRejectsABlankName(string name)
    {
        ValidationResult result = validator.Validate(new CreateSpaceCommand(name));

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(CreateSpaceCommand.Name));
    }

    [TestMethod]
    public void ValidateRejectsANameOverTheMaximumTrimmedLength()
    {
        CreateSpaceCommand command = new CreateSpaceCommand(
            new string('n', SpaceValidationLimits.NameMaximumLength + 1));

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(CreateSpaceCommand.Name));
    }
}
