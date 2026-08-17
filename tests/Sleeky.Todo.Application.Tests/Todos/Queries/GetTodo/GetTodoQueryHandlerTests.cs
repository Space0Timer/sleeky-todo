using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Queries.GetTodo;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Tests.Todos.Queries.GetTodo;

[TestClass]
public sealed class GetTodoQueryHandlerTests
{
    [TestMethod]
    public async Task HandleReturnsMappedTodo()
    {
        TodoItem todoItem = TestTodoFactory.Create();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        repository
            .GetByIdAsync(todoItem.Id, false, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        GetTodoQueryHandler handler = new GetTodoQueryHandler(repository);
        GetTodoQuery query = new GetTodoQuery(TestTodoFactory.SpaceId, todoItem.Id);

        TodoDto result = await handler.Handle(query, CancellationToken.None);

        result.Id.Should().Be(todoItem.Id);
        result.Name.Should().Be(todoItem.Name);
        result.Version.Should().Be(todoItem.Version);
    }

    [TestMethod]
    public async Task HandleThrowsNotFoundWhenTodoDoesNotExist()
    {
        Guid todoId = TestTodoFactory.CreateId("missing-todo");
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        repository
            .GetByIdAsync(todoId, false, Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);
        GetTodoQueryHandler handler = new GetTodoQueryHandler(repository);

        Func<Task> act = async () =>
            await handler.Handle(new GetTodoQuery(TestTodoFactory.SpaceId, todoId), CancellationToken.None);

        NotFoundException exception = (await act.Should()
            .ThrowAsync<NotFoundException>())
            .Which;
        exception.ResourceId.Should().Be(todoId);
    }

    [TestMethod]
    public async Task HandleExcludesDeletedTodosFromNormalRetrieval()
    {
        TodoItem deletedTodo = TestTodoFactory.CreateDeleted();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        repository
            .GetByIdAsync(deletedTodo.Id, false, Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);
        GetTodoQueryHandler handler = new GetTodoQueryHandler(repository);

        Func<Task> act = async () =>
            await handler.Handle(
                new GetTodoQuery(TestTodoFactory.SpaceId, deletedTodo.Id),
                CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await repository.Received(1).GetByIdAsync(
            deletedTodo.Id,
            false,
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().GetByIdAsync(
            deletedTodo.Id,
            true,
            Arg.Any<CancellationToken>());
    }
}
