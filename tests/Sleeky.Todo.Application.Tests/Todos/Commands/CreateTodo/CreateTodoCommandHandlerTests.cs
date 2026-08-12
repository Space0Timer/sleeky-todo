using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.CreateTodo;

[TestClass]
public sealed class CreateTodoCommandHandlerTests
{
    [TestMethod]
    public async Task HandleCreatesPersistsAndReturnsTodo()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestTodoFactory.Timestamp);
        CreateTodoCommand command = new CreateTodoCommand(
            "  Submit report  ",
            "  Monthly report  ",
            TestTodoFactory.DueDate,
            TodoPriority.High);
        CreateTodoCommandHandler handler = new CreateTodoCommandHandler(repository, clock);
        CancellationToken cancellationToken = new CancellationTokenSource().Token;

        TodoDto result = await handler.Handle(command, cancellationToken);

        result.Id.Should().HaveLength(32);
        result.Name.Should().Be("Submit report");
        result.Description.Should().Be("Monthly report");
        result.Version.Should().Be(1);
        result.CreatedAt.Should().Be(TestTodoFactory.Timestamp);
        await repository.Received(1).AddAsync(
            Arg.Is<TodoItem>(todoItem => todoItem.Id == result.Id),
            cancellationToken);
    }

    [TestMethod]
    public async Task HandleCreatesFirstRecurringOccurrenceWithSeriesIdentity()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestTodoFactory.Timestamp);
        CreateTodoCommand command = new CreateTodoCommand(
            "Submit report",
            "Monthly report",
            TestTodoFactory.DueDate,
            TodoPriority.High,
            RecurrenceType.Monthly,
            1);
        CreateTodoCommandHandler handler = new CreateTodoCommandHandler(repository, clock);

        TodoDto result = await handler.Handle(command, CancellationToken.None);

        result.Recurrence.Should().NotBeNull();
        result.Recurrence!.Type.Should().Be(RecurrenceType.Monthly);
        result.Recurrence.AnchorDay.Should().Be(31);
        result.SeriesId.Should().HaveLength(32);
        result.OccurrenceNumber.Should().Be(1);
    }
}
