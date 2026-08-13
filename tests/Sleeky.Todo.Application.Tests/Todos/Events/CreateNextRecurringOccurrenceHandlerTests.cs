using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Todos.Events;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Events;
using Sleeky.Todo.Domain.Services;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Tests.Todos.Events;

[TestClass]
public sealed class CreateNextRecurringOccurrenceHandlerTests
{
    [TestMethod]
    public async Task RecurringCompletionCreatesNextOccurrenceWithoutDependencies()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            TestTodoFactory.DueDate);
        TodoCompletedDomainEvent domainEvent = new TodoCompletedDomainEvent(
            TestTodoFactory.CreateId("todo-1"),
            TestTodoFactory.CreateId("series-1"),
            1,
            TestTodoFactory.CreateId("todo-2"),
            new TodoCompletionContext(
                TestTodoFactory.OwnerId,
                "Submit report",
                "Monthly report",
                TestTodoFactory.DueDate,
                TodoPriority.High,
                recurrence,
                TestTodoFactory.Timestamp));
        TodoItem? captured = null;
        repository.AddAsync(
                Arg.Do<TodoItem>(todo => captured = todo),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        CreateNextRecurringOccurrenceHandler handler =
            new CreateNextRecurringOccurrenceHandler(
                repository,
                new RecurrenceCalculator());

        await handler.HandleAsync(domainEvent, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Id.Should().Be(TestTodoFactory.CreateId("todo-2"));
        captured.DueDate.Should().Be(new DateOnly(2026, 9, 30));
        captured.SeriesId.Should().Be(TestTodoFactory.CreateId("series-1"));
        captured.OccurrenceNumber.Should().Be(2);
        captured.Recurrence.Should().Be(recurrence);
        captured.DependencyIds.Should().BeEmpty();
        captured.Status.Should().Be(TodoStatus.NotStarted);
    }

    [TestMethod]
    public async Task NonRecurringCompletionCreatesNothing()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        TodoCompletedDomainEvent domainEvent = new TodoCompletedDomainEvent(
            TestTodoFactory.CreateId("todo-1"),
            null,
            null,
            null,
            new TodoCompletionContext(
                TestTodoFactory.OwnerId,
                "Submit report",
                null,
                TestTodoFactory.DueDate,
                TodoPriority.High,
                null,
                TestTodoFactory.Timestamp));
        CreateNextRecurringOccurrenceHandler handler =
            new CreateNextRecurringOccurrenceHandler(
                repository,
                new RecurrenceCalculator());

        await handler.HandleAsync(domainEvent, CancellationToken.None);

        await repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
