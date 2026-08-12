using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Todos.Commands.DeleteTodo;
using Sleeky.Todo.Application.Todos.Commands.RestoreTodo;

namespace Sleeky.Todo.Application.Tests.Todos.Commands;

[TestClass]
public sealed class VersionedTodoCommandValidatorTests
{
    [TestMethod]
    public void DeleteValidatorRejectsInvalidIdentifierAndVersion()
    {
        DeleteTodoCommand command = new DeleteTodoCommand(Guid.Empty, 0);
        DeleteTodoCommandValidator validator = new DeleteTodoCommandValidator();

        ValidationResult result = validator.Validate(command);

        result.Errors
            .Select(failure => failure.PropertyName)
            .Should()
            .BeEquivalentTo(
                nameof(DeleteTodoCommand.Id),
                nameof(DeleteTodoCommand.Version));
    }

    [TestMethod]
    public void RestoreValidatorRejectsInvalidIdentifierAndVersion()
    {
        RestoreTodoCommand command = new RestoreTodoCommand(Guid.Empty, -1);
        RestoreTodoCommandValidator validator = new RestoreTodoCommandValidator();

        ValidationResult result = validator.Validate(command);

        result.Errors
            .Select(failure => failure.PropertyName)
            .Should()
            .BeEquivalentTo(
                nameof(RestoreTodoCommand.Id),
                nameof(RestoreTodoCommand.Version));
    }

    [TestMethod]
    public void DeleteAndRestoreValidatorsAcceptValidCommands()
    {
        ValidationResult deleteResult = new DeleteTodoCommandValidator()
            .Validate(new DeleteTodoCommand(TestTodoFactory.CreateId("todo-1"), 1));
        ValidationResult restoreResult = new RestoreTodoCommandValidator()
            .Validate(new RestoreTodoCommand(TestTodoFactory.CreateId("todo-1"), 1));

        deleteResult.IsValid.Should().BeTrue();
        restoreResult.IsValid.Should().BeTrue();
    }
}
