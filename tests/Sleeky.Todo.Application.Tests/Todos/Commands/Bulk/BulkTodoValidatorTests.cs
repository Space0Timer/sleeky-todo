using FluentAssertions;

using FluentValidation.Results;

using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Commands.BulkChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Commands.BulkDeleteTodos;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.Bulk;

[TestClass]
public sealed class BulkTodoValidatorTests
{
    [TestMethod]
    public void EmptySelectionIsRejected()
    {
        ValidationResult result = Validate(Array.Empty<BulkTodoItemRequest>());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("At least one TODO must be selected.");
    }

    [TestMethod]
    public void SelectionAboveTheLimitIsRejected()
    {
        BulkTodoItemRequest[] items = Enumerable.Range(0, 101)
            .Select(index => new BulkTodoItemRequest(
                TestTodoFactory.CreateId($"todo-{index}"),
                1))
            .ToArray();

        ValidationResult result = Validate(items);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("No more than 100 TODOs can be selected.");
    }

    [TestMethod]
    public void SelectionAtTheLimitIsAccepted()
    {
        BulkTodoItemRequest[] items = Enumerable.Range(0, 100)
            .Select(index => new BulkTodoItemRequest(
                TestTodoFactory.CreateId($"todo-{index}"),
                1))
            .ToArray();

        Validate(items).IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void DuplicateSelectionIsRejected()
    {
        Guid id = TestTodoFactory.CreateId("todo-1");

        ValidationResult result = Validate(
        [
            new BulkTodoItemRequest(id, 1),
            new BulkTodoItemRequest(id, 2),
        ]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("A TODO can only be selected once.");
    }

    [TestMethod]
    public void EmptyIdentifierIsRejected()
    {
        ValidationResult result = Validate([new BulkTodoItemRequest(Guid.Empty, 1)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("A TODO identifier is required.");
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void NonPositiveVersionIsRejected(long version)
    {
        ValidationResult result = Validate(
            [new BulkTodoItemRequest(TestTodoFactory.CreateId("todo-1"), version)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Expected version must be greater than zero.");
    }

    [TestMethod]
    [DataRow(TodoStatus.Completed)]
    [DataRow(TodoStatus.Archived)]
    [DataRow(TodoStatus.NotStarted)]
    [DataRow(TodoStatus.InProgress)]
    public void SupportedBulkStatusesAreAccepted(TodoStatus status)
    {
        BulkChangeTodoStatusCommand command = new BulkChangeTodoStatusCommand(
            status,
            [new BulkTodoItemRequest(TestTodoFactory.CreateId("todo-1"), 1)]);

        new BulkChangeTodoStatusCommandValidator().Validate(command)
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void UnknownBulkStatusIsRejected()
    {
        BulkChangeTodoStatusCommand command = new BulkChangeTodoStatusCommand(
            (TodoStatus)99,
            [new BulkTodoItemRequest(TestTodoFactory.CreateId("todo-1"), 1)]);

        ValidationResult result = new BulkChangeTodoStatusCommandValidator()
            .Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should()
            .Be("A bulk status change must target a known status.");
    }

    private static ValidationResult Validate(IReadOnlyCollection<BulkTodoItemRequest> items)
    {
        return new BulkDeleteTodosCommandValidator()
            .Validate(new BulkDeleteTodosCommand(items));
    }
}
