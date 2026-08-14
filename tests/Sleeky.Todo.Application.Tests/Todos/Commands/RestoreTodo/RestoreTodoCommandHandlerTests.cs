using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.RestoreTodo;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.RestoreTodo;

[TestClass]
public sealed class RestoreTodoCommandHandlerTests
{
    [TestMethod]
    public async Task HandleThrowsNotFoundWhenTodoDoesNotExist()
    {
        Guid todoId = TestTodoFactory.CreateId("missing-todo");
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        repository
            .GetByIdAsync(todoId, true, Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);
        RestoreTodoCommand command = new RestoreTodoCommand(todoId, 1);
        RestoreTodoCommandHandler handler = CreateHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        NotFoundException exception = (await act.Should()
            .ThrowAsync<NotFoundException>())
            .Which;
        exception.ResourceId.Should().Be(todoId);
        await repository.DidNotReceiveWithAnyArgs().RestoreAsync(
            default!,
            default);
    }

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
            .RestoreAsync(todoItem, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(todoItem, 2));
        RestoreTodoCommand command = new RestoreTodoCommand(todoItem.Id, todoItem.Version);
        RestoreTodoCommandHandler handler = CreateHandler(repository, clock);

        TodoDto result = await handler.Handle(command, CancellationToken.None);

        result.DeletedAt.Should().BeNull();
        result.PurgeAt.Should().BeNull();
        result.UpdatedAt.Should().Be(restoredAt);
        result.Version.Should().Be(2);
        await repository.Received(1).GetByIdAsync(
            todoItem.Id,
            true,
            Arg.Any<CancellationToken>());
        await repository.Received(1).RestoreAsync(
            todoItem,
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
        RestoreTodoCommandHandler handler = CreateHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        await repository.DidNotReceiveWithAnyArgs().RestoreAsync(
            default!,
            default);
    }

    [TestMethod]
    public async Task HandleThrowsConcurrencyWhenAtomicRestoreLosesRace()
    {
        TodoItem todoItem = TestTodoFactory.CreateDeleted();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestTodoFactory.Timestamp.AddDays(2));
        repository
            .GetByIdAsync(todoItem.Id, true, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        repository
            .RestoreAsync(todoItem, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException(
                "TODO",
                todoItem.Id,
                todoItem.Version));
        RestoreTodoCommand command = new RestoreTodoCommand(todoItem.Id, todoItem.Version);
        RestoreTodoCommandHandler handler = CreateHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [TestMethod]
    public async Task HandleRejectsActiveTodoWithoutCallingRestoreRepositoryMethod()
    {
        TodoItem todoItem = TestTodoFactory.Create();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestTodoFactory.Timestamp.AddDays(1));
        repository
            .GetByIdAsync(todoItem.Id, true, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        RestoreTodoCommand command = new RestoreTodoCommand(todoItem.Id, todoItem.Version);
        RestoreTodoCommandHandler handler = CreateHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("Only a deleted TODO can be restored.");
        await repository.DidNotReceiveWithAnyArgs().RestoreAsync(
            default!,
            default);
    }

    [TestMethod]
    public async Task HandleRejectsTodoAtRetentionBoundaryWithoutCallingRepositoryMethod()
    {
        TodoItem todoItem = TestTodoFactory.CreateDeleted();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(todoItem.PurgeAt!.Value);
        repository
            .GetByIdAsync(todoItem.Id, true, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        RestoreTodoCommand command = new RestoreTodoCommand(todoItem.Id, todoItem.Version);
        RestoreTodoCommandHandler handler = CreateHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("The TODO retention period has expired.");
        await repository.DidNotReceiveWithAnyArgs().RestoreAsync(
            default!,
            default);
    }

    private static RestoreTodoCommandHandler CreateHandler(
        ITodoRepository repository,
        IClock clock)
    {
        return new RestoreTodoCommandHandler(
            repository,
            clock,
            NullLogger<RestoreTodoCommandHandler>.Instance);
    }
}
