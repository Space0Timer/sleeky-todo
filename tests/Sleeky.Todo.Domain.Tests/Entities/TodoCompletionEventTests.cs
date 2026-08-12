using FluentAssertions;

using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Events;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Tests.Entities;

[TestClass]
public sealed class TodoCompletionEventTests
{
    private static readonly DateOnly DueDate = new DateOnly(2026, 8, 31);
    private static readonly DateTimeOffset Timestamp = new DateTimeOffset(
        2026,
        8,
        12,
        9,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public void TransitionToCompletedRaisesEventWithRecurrenceContext()
    {
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            DueDate);
        TodoItem todo = TodoItem.Create(
            "todo-1",
            "Submit report",
            "Monthly report",
            DueDate,
            TodoPriority.High,
            Timestamp,
            recurrence,
            "series-1",
            1);

        bool changed = todo.ChangeStatus(TodoStatus.Completed, Timestamp.AddDays(20));

        changed.Should().BeTrue();
        TodoCompletedDomainEvent domainEvent = todo.DomainEvents
            .Should().ContainSingle().Subject
            .Should().BeOfType<TodoCompletedDomainEvent>().Subject;
        domainEvent.TodoId.Should().Be(todo.Id);
        domainEvent.SeriesId.Should().Be("series-1");
        domainEvent.OccurrenceNumber.Should().Be(1);
        domainEvent.NextOccurrenceId.Should().HaveLength(32);
        domainEvent.CompletionContext.ScheduledDueDate.Should().Be(DueDate);
        domainEvent.CompletionContext.Recurrence.Should().Be(recurrence);
    }

    [TestMethod]
    public void CompletedToCompletedDoesNotRaiseAnotherEvent()
    {
        TodoItem todo = TodoItem.Create(
            "todo-1",
            "Submit report",
            null,
            DueDate,
            TodoPriority.High,
            Timestamp);
        _ = todo.ChangeStatus(TodoStatus.Completed, Timestamp.AddHours(1));
        todo.ClearDomainEvents();

        bool changed = todo.ChangeStatus(TodoStatus.Completed, Timestamp.AddHours(2));

        changed.Should().BeFalse();
        todo.DomainEvents.Should().BeEmpty();
    }
}
