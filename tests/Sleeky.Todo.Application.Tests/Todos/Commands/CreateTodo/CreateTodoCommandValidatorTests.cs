using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
using Sleeky.Todo.Application.Todos.Validation;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.CreateTodo;

[TestClass]
public sealed class CreateTodoCommandValidatorTests
{
    private readonly CreateTodoCommandValidator validator = new CreateTodoCommandValidator();

    [TestMethod]
    public void ValidateAcceptsTrimmedBoundaryLengths()
    {
        CreateTodoCommand command = new CreateTodoCommand(
            TestTodoFactory.SpaceId,
            $"  {new string('n', TodoValidationLimits.NameMaximumLength)}  ",
            $"  {new string('d', TodoValidationLimits.DescriptionMaximumLength)}  ",
            TestTodoFactory.DueDate,
            TodoPriority.Medium);

        ValidationResult result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void ValidateRejectsWhitespaceOnlyName()
    {
        CreateTodoCommand command = CreateCommand(name: "   ");

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(CreateTodoCommand.Name));
    }

    [TestMethod]
    public void ValidateRejectsNameOverMaximumTrimmedLength()
    {
        CreateTodoCommand command = CreateCommand(
            name: new string('n', TodoValidationLimits.NameMaximumLength + 1));

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(CreateTodoCommand.Name));
    }

    [TestMethod]
    public void ValidateRejectsDescriptionOverMaximumTrimmedLength()
    {
        CreateTodoCommand command = CreateCommand(
            description: new string('d', TodoValidationLimits.DescriptionMaximumLength + 1));

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(CreateTodoCommand.Description));
    }

    [TestMethod]
    public void ValidateRejectsDefaultDueDate()
    {
        CreateTodoCommand command = new CreateTodoCommand(
            TestTodoFactory.SpaceId,
            "Submit report",
            "Monthly report",
            default,
            TodoPriority.High);

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(CreateTodoCommand.DueDate));
    }

    [TestMethod]
    public void ValidateRejectsUndefinedPriority()
    {
        CreateTodoCommand command = CreateCommand(priority: (TodoPriority)999);

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(CreateTodoCommand.Priority));
    }

    [TestMethod]
    public void ValidateAcceptsCustomRecurrence()
    {
        CreateTodoCommand command = new CreateTodoCommand(
            TestTodoFactory.SpaceId,
            "Submit report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.High,
            RecurrenceType.Custom,
            3,
            RecurrenceUnit.Weeks);

        ValidationResult result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void ValidateRejectsInvalidRecurrenceInputs()
    {
        CreateTodoCommand missingUnit = new CreateTodoCommand(
            TestTodoFactory.SpaceId,
            "Submit report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.High,
            RecurrenceType.Custom,
            2);
        CreateTodoCommand zeroInterval = new CreateTodoCommand(
            TestTodoFactory.SpaceId,
            "Submit report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.High,
            RecurrenceType.Custom,
            0,
            RecurrenceUnit.Days);
        CreateTodoCommand nonStandardInterval = new CreateTodoCommand(
            TestTodoFactory.SpaceId,
            "Submit report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.High,
            RecurrenceType.Daily,
            2);
        CreateTodoCommand mismatchedUnit = new CreateTodoCommand(
            TestTodoFactory.SpaceId,
            "Submit report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.High,
            RecurrenceType.Weekly,
            1,
            RecurrenceUnit.Months);
        CreateTodoCommand oversizedInterval = new CreateTodoCommand(
            TestTodoFactory.SpaceId,
            "Submit report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.High,
            RecurrenceType.Custom,
            RecurrenceSchedule.MaximumInterval + 1,
            RecurrenceUnit.Days);

        validator.Validate(missingUnit).IsValid.Should().BeFalse();
        validator.Validate(zeroInterval).IsValid.Should().BeFalse();
        validator.Validate(nonStandardInterval).IsValid.Should().BeFalse();
        validator.Validate(mismatchedUnit).IsValid.Should().BeFalse();
        validator.Validate(oversizedInterval).Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be(
                $"The recurrence interval must not exceed {RecurrenceSchedule.MaximumInterval}.");
    }

    private static CreateTodoCommand CreateCommand(
        string name = "Submit report",
        string? description = "Monthly report",
        TodoPriority priority = TodoPriority.High)
    {
        return new CreateTodoCommand(
            TestTodoFactory.SpaceId,
            name,
            description,
            TestTodoFactory.DueDate,
            priority);
    }
}
