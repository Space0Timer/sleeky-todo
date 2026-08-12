using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
using Sleeky.Todo.Application.Todos.Validation;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.CreateTodo;

[TestClass]
public sealed class CreateTodoCommandValidatorTests
{
    private readonly CreateTodoCommandValidator validator = new CreateTodoCommandValidator();

    [TestMethod]
    public void ValidateAcceptsTrimmedBoundaryLengths()
    {
        CreateTodoCommand command = new CreateTodoCommand(
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

    private static CreateTodoCommand CreateCommand(
        string name = "Submit report",
        string? description = "Monthly report",
        TodoPriority priority = TodoPriority.High)
    {
        return new CreateTodoCommand(
            name,
            description,
            TestTodoFactory.DueDate,
            priority);
    }
}
