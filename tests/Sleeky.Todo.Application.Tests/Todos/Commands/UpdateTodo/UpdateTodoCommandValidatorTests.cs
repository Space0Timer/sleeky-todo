using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Todos.Commands.UpdateTodo;
using Sleeky.Todo.Application.Todos.Validation;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.UpdateTodo;

[TestClass]
public sealed class UpdateTodoCommandValidatorTests
{
    private readonly UpdateTodoCommandValidator validator = new UpdateTodoCommandValidator();

    [TestMethod]
    public void ValidateAcceptsValidCommand()
    {
        UpdateTodoCommand command = CreateCommand();

        ValidationResult result = validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void ValidateRejectsEveryInvalidField()
    {
        UpdateTodoCommand command = new UpdateTodoCommand(
            Guid.Empty,
            "   ",
            new string('d', TodoValidationLimits.DescriptionMaximumLength + 1),
            default,
            (TodoPriority)999,
            0);

        ValidationResult result = validator.Validate(command);

        result.Errors
            .Select(failure => failure.PropertyName)
            .Should()
            .BeEquivalentTo(
                nameof(UpdateTodoCommand.Id),
                nameof(UpdateTodoCommand.Name),
                nameof(UpdateTodoCommand.Description),
                nameof(UpdateTodoCommand.DueDate),
                nameof(UpdateTodoCommand.Priority),
                nameof(UpdateTodoCommand.Version));
    }

    [TestMethod]
    public void ValidateRejectsNegativeVersion()
    {
        UpdateTodoCommand command = CreateCommand(version: -1);

        ValidationResult result = validator.Validate(command);

        result.Errors.Should().ContainSingle(
            failure => failure.PropertyName == nameof(UpdateTodoCommand.Version));
    }

    private static UpdateTodoCommand CreateCommand(
        Guid? id = null,
        long version = 1)
    {
        return new UpdateTodoCommand(
            id ?? TestTodoFactory.CreateId("todo-1"),
            "Submit report",
            "Monthly report",
            TestTodoFactory.DueDate,
            TodoPriority.High,
            version);
    }
}
