using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.DeleteTodo;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.DeleteTodo;

[TestClass]
public sealed class DeleteTodoCommandHandlerTests
{
    [TestMethod]
    public async Task HandleThrowsNotFoundWhenTodoDoesNotExist()
    {
        Guid todoId = TestTodoFactory.CreateId("missing-todo");
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        repository
            .GetByIdAsync(todoId, false, Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);
        DeleteTodoCommand command = new DeleteTodoCommand(TestTodoFactory.SpaceId, todoId, 1);
        DeleteTodoCommandHandler handler = CreateHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        NotFoundException exception = (await act.Should()
            .ThrowAsync<NotFoundException>())
            .Which;
        exception.ResourceId.Should().Be(todoId);
        await repository.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            default!,
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
            .SoftDeleteAsync(todoItem, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(todoItem, 2));
        DeleteTodoCommand command = new DeleteTodoCommand(TestTodoFactory.SpaceId, todoItem.Id, todoItem.Version);
        DeleteTodoCommandHandler handler = CreateHandler(repository, clock);

        TodoDto result = await handler.Handle(command, CancellationToken.None);

        result.DeletedAt.Should().Be(deletedAt);
        result.PurgeAt.Should().Be(deletedAt.AddDays(90));
        result.Version.Should().Be(2);
        await repository.Received(1).SoftDeleteAsync(
            todoItem,
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
        DeleteTodoCommand command = new DeleteTodoCommand(TestTodoFactory.SpaceId, todoItem.Id, todoItem.Version + 1);
        DeleteTodoCommandHandler handler = CreateHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        await repository.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            default!,
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
            .SoftDeleteAsync(todoItem, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException(
                "TODO",
                todoItem.Id,
                todoItem.Version));
        DeleteTodoCommand command = new DeleteTodoCommand(TestTodoFactory.SpaceId, todoItem.Id, todoItem.Version);
        DeleteTodoCommandHandler handler = CreateHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [TestMethod]
    public async Task HandleRejectsTodoWithActiveDependents()
    {
        TodoItem todoItem = TestTodoFactory.Create();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        repository
            .GetByIdAsync(todoItem.Id, false, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        repository.HasActiveDependentsAsync(
                todoItem.Id,
                Arg.Any<CancellationToken>())
            .Returns(true);
        DeleteTodoCommandHandler handler = CreateHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(
            new DeleteTodoCommand(TestTodoFactory.SpaceId, todoItem.Id, todoItem.Version),
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("A TODO with active dependents cannot be deleted.");
        await repository.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            default!,
            default);
    }

    private static DeleteTodoCommandHandler CreateHandler(
        ITodoRepository repository,
        IClock clock)
    {
        return new DeleteTodoCommandHandler(
            repository,
            clock,
            NullLogger<DeleteTodoCommandHandler>.Instance);
    }
}
