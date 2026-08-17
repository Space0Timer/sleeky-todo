using FluentAssertions;

using FluentValidation;
using FluentValidation.Results;

using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Application.Todos.Commands.AddDependency;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Commands.Bulk.ChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Commands.Bulk.DeleteTodos;
using Sleeky.Todo.Application.Todos.Commands.Bulk.RestoreTodos;
using Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
using Sleeky.Todo.Application.Todos.Commands.DeleteTodo;
using Sleeky.Todo.Application.Todos.Commands.RemoveDependency;
using Sleeky.Todo.Application.Todos.Commands.RestoreTodo;
using Sleeky.Todo.Application.Todos.Commands.UpdateTodo;
using Sleeky.Todo.Application.Todos.Queries.GetTodo;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Todos;

/// <summary>
/// The rule every TODO request shares: it names a Space, asks for the level
/// its kind of work needs, and refuses to run without one. Stated once here
/// across the whole set, so a new request that forgets either half fails a
/// test rather than reading every Space or none.
/// </summary>
[TestClass]
public sealed class SpaceScopedTodoRequestTests
{
    private static readonly Guid TodoId = TestTodoFactory.CreateId("todo-1");
    private static readonly Guid DependencyId = TestTodoFactory.CreateId("todo-2");

    private static readonly BulkTodoItemRequest[] Selection =
    [
        new BulkTodoItemRequest(TodoId, 1),
    ];

    public static IEnumerable<object[]> Queries()
    {
        yield return Case(
            new GetTodoQuery(Guid.Empty, TodoId),
            new GetTodoQueryValidator());
        yield return Case(
            new GetTodosQuery(Guid.Empty),
            new GetTodosQueryValidator());
        yield return Case(
            new GetTodoSelectionQuery(Guid.Empty, [TodoId]),
            new GetTodoSelectionQueryValidator());
    }

    public static IEnumerable<object[]> Commands()
    {
        yield return Case(
            new CreateTodoCommand(Guid.Empty, "Submit report", null, TestTodoFactory.DueDate, TodoPriority.High),
            new CreateTodoCommandValidator());
        yield return Case(
            new UpdateTodoCommand(Guid.Empty, TodoId, "Submit report", null, TestTodoFactory.DueDate, TodoPriority.High, 1),
            new UpdateTodoCommandValidator());
        yield return Case(
            new ChangeTodoStatusCommand(Guid.Empty, TodoId, TodoStatus.Completed, 1),
            new ChangeTodoStatusCommandValidator());
        yield return Case(
            new DeleteTodoCommand(Guid.Empty, TodoId, 1),
            new DeleteTodoCommandValidator());
        yield return Case(
            new RestoreTodoCommand(Guid.Empty, TodoId, 1),
            new RestoreTodoCommandValidator());
        yield return Case(
            new AddDependencyCommand(Guid.Empty, TodoId, DependencyId, 1),
            new AddDependencyCommandValidator());
        yield return Case(
            new RemoveDependencyCommand(Guid.Empty, TodoId, DependencyId, 1),
            new RemoveDependencyCommandValidator());
        yield return Case(
            new BulkChangeTodoStatusCommand(Guid.Empty, TodoStatus.Completed, Selection),
            new BulkChangeTodoStatusCommandValidator());
        yield return Case(
            new BulkDeleteTodosCommand(Guid.Empty, Selection),
            new BulkDeleteTodosCommandValidator());
        yield return Case(
            new BulkRestoreTodosCommand(Guid.Empty, Selection),
            new BulkRestoreTodosCommandValidator());
    }

    [TestMethod]
    [DynamicData(nameof(Queries))]
    public void QueriesAskForReadAccess(ISpaceScopedRequest request, IValidator validator)
    {
        request.RequiredPermission.Should().Be(SpacePermission.Read);
    }

    [TestMethod]
    [DynamicData(nameof(Commands))]
    public void CommandsAskForWriteAccess(ISpaceScopedRequest request, IValidator validator)
    {
        request.RequiredPermission.Should().Be(SpacePermission.Write);
    }

    [TestMethod]
    [DynamicData(nameof(Queries))]
    [DynamicData(nameof(Commands))]
    public void ValidatorsRejectAnEmptySpace(ISpaceScopedRequest request, IValidator validator)
    {
        ValidationResult result = validator.Validate(new ValidationContext<object>(request));

        result.Errors.Should().ContainSingle(failure =>
            failure.PropertyName == nameof(ISpaceScopedRequest.SpaceId)
            && failure.ErrorMessage == "A Space identifier is required.");
    }

    private static object[] Case(ISpaceScopedRequest request, IValidator validator)
    {
        return [request, validator];
    }
}
