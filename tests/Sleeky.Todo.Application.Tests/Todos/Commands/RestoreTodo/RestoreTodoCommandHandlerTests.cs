using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.RestoreTodo;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.RestoreTodo;

[TestClass]
public sealed class RestoreTodoCommandHandlerTests
{
    [TestMethod]
    public async Task HandleLoadsDeletedTodoAndReturnsRestoredTodo()
    {
        TodoItem todoItem = TestTodoFactory.CreateDeleted();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        DateTimeOffset restoredAt = TestTodoFactory.Timestamp.AddDays(2);
        clock.UtcNow.Returns(restoredAt);
        repository
            .GetByIdAsync(todoItem.Id, true, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        repository
            .RestoreAsync(todoItem, todoItem.Version, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        RestoreTodoCommand command = new RestoreTodoCommand(todoItem.Id, todoItem.Version);
        RestoreTodoCommandHandler handler = new RestoreTodoCommandHandler(repository, clock);

        TodoDto result = await handler.Handle(command, CancellationToken.None);

        result.DeletedAt.Should().BeNull();
        result.PurgeAt.Should().BeNull();
        result.UpdatedAt.Should().Be(restoredAt);
        await repository.Received(1).GetByIdAsync(
            todoItem.Id,
            true,
            Arg.Any<CancellationToken>());
        await repository.Received(1).RestoreAsync(
            todoItem,
            command.Version,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task HandleThrowsConcurrencyWhenClientVersionIsStale()
    {
        TodoItem todoItem = TestTodoFactory.CreateDeleted();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        repository
            .GetByIdAsync(todoItem.Id, true, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        RestoreTodoCommand command = new RestoreTodoCommand(todoItem.Id, todoItem.Version + 1);
        RestoreTodoCommandHandler handler = new RestoreTodoCommandHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<TodoConcurrencyException>();
        await repository.DidNotReceiveWithAnyArgs().RestoreAsync(
            default!,
            default,
            default);
    }
}
