using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.DeleteTodo;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.DeleteTodo;

[TestClass]
public sealed class DeleteTodoCommandHandlerTests
{
    [TestMethod]
    public async Task HandleThrowsNotFoundWhenTodoDoesNotExist()
    {
        const string TodoId = "missing-todo";
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        repository
            .GetByIdAsync(TodoId, false, Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);
        DeleteTodoCommand command = new DeleteTodoCommand(TodoId, 1);
        DeleteTodoCommandHandler handler = new DeleteTodoCommandHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        NotFoundException exception = (await act.Should()
            .ThrowAsync<NotFoundException>())
            .Which;
        exception.ResourceId.Should().Be(TodoId);
        await repository.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            default!,
            default,
            default);
    }

    [TestMethod]
    public async Task HandleSoftDeletesWithExpectedVersionAndReturnsDeletedTodo()
    {
        TodoItem todoItem = TestTodoFactory.Create();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        DateTimeOffset deletedAt = TestTodoFactory.Timestamp.AddDays(1);
        clock.UtcNow.Returns(deletedAt);
        repository
            .GetByIdAsync(todoItem.Id, false, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        repository
            .SoftDeleteAsync(todoItem, todoItem.Version, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(todoItem, 2));
        DeleteTodoCommand command = new DeleteTodoCommand(todoItem.Id, todoItem.Version);
        DeleteTodoCommandHandler handler = new DeleteTodoCommandHandler(repository, clock);

        TodoDto result = await handler.Handle(command, CancellationToken.None);

        result.DeletedAt.Should().Be(deletedAt);
        result.PurgeAt.Should().Be(deletedAt.AddDays(90));
        result.Version.Should().Be(2);
        await repository.Received(1).SoftDeleteAsync(
            todoItem,
            command.Version,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task HandleThrowsConcurrencyWhenClientVersionIsStale()
    {
        TodoItem todoItem = TestTodoFactory.Create();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        repository
            .GetByIdAsync(todoItem.Id, false, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        DeleteTodoCommand command = new DeleteTodoCommand(todoItem.Id, todoItem.Version + 1);
        DeleteTodoCommandHandler handler = new DeleteTodoCommandHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        await repository.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            default!,
            default,
            default);
    }

    [TestMethod]
    public async Task HandleThrowsConcurrencyWhenAtomicDeleteLosesRace()
    {
        TodoItem todoItem = TestTodoFactory.Create();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestTodoFactory.Timestamp.AddDays(1));
        repository
            .GetByIdAsync(todoItem.Id, false, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        repository
            .SoftDeleteAsync(todoItem, todoItem.Version, Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);
        DeleteTodoCommand command = new DeleteTodoCommand(todoItem.Id, todoItem.Version);
        DeleteTodoCommandHandler handler = new DeleteTodoCommandHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }
}
