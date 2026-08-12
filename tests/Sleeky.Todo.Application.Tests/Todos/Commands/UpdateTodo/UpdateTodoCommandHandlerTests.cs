using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.UpdateTodo;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.UpdateTodo;

[TestClass]
public sealed class UpdateTodoCommandHandlerTests
{
    [TestMethod]
    public async Task HandleUpdatesWithExpectedVersionAndReturnsRepositoryResult()
    {
        TodoItem todoItem = TestTodoFactory.Create();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        DateTimeOffset updatedAt = TestTodoFactory.Timestamp.AddHours(2);
        clock.UtcNow.Returns(updatedAt);
        repository
            .GetByIdAsync(todoItem.Id, false, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        repository
            .UpdateAsync(todoItem, todoItem.Version, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(todoItem, 2));
        UpdateTodoCommand command = new UpdateTodoCommand(
            todoItem.Id,
            "Review report",
            "Revised report",
            TestTodoFactory.DueDate.AddDays(1),
            TodoPriority.Medium,
            todoItem.Version);
        UpdateTodoCommandHandler handler = new UpdateTodoCommandHandler(repository, clock);

        TodoDto result = await handler.Handle(command, CancellationToken.None);

        result.Name.Should().Be("Review report");
        result.Description.Should().Be("Revised report");
        result.UpdatedAt.Should().Be(updatedAt);
        result.Version.Should().Be(2);
        await repository.Received(1).UpdateAsync(
            todoItem,
            command.Version,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task HandleThrowsNotFoundWhenTodoDoesNotExist()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        repository
            .GetByIdAsync("missing-todo", false, Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);
        UpdateTodoCommand command = CreateCommand("missing-todo", 1);
        UpdateTodoCommandHandler handler = new UpdateTodoCommandHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(
            default!,
            default,
            default);
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
        UpdateTodoCommand command = CreateCommand(todoItem.Id, todoItem.Version + 1);
        UpdateTodoCommandHandler handler = new UpdateTodoCommandHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        ConcurrencyConflictException exception = (await act.Should()
            .ThrowAsync<ConcurrencyConflictException>())
            .Which;
        exception.ExpectedVersion.Should().Be(command.Version);
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(
            default!,
            default,
            default);
    }

    [TestMethod]
    public async Task HandleThrowsConcurrencyWhenAtomicUpdateLosesRace()
    {
        TodoItem todoItem = TestTodoFactory.Create();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestTodoFactory.Timestamp.AddHours(1));
        repository
            .GetByIdAsync(todoItem.Id, false, Arg.Any<CancellationToken>())
            .Returns(todoItem);
        repository
            .UpdateAsync(todoItem, todoItem.Version, Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);
        UpdateTodoCommand command = CreateCommand(todoItem.Id, todoItem.Version);
        UpdateTodoCommandHandler handler = new UpdateTodoCommandHandler(repository, clock);

        Func<Task> act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    private static UpdateTodoCommand CreateCommand(string id, long version)
    {
        return new UpdateTodoCommand(
            id,
            "Review report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.Low,
            version);
    }
}
