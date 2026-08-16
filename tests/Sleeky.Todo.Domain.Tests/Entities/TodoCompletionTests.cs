using FluentAssertions;

using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Tests.Entities;

[TestClass]
public sealed class TodoCompletionTests
{
    private static readonly DateOnly DueDate = new DateOnly(2026, 8, 31);
    private static readonly Guid TodoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeriesId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OwnerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Timestamp = new DateTimeOffset(
        2026,
        8,
        12,
        9,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public void TransitionToCompletedRecordsCompletionWithRecurrenceContext()
    {
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            DueDate);
        TodoItem todo = TodoItem.Create(
            TodoId,
            OwnerId,
            "Submit report",
            "Monthly report",
            DueDate,
            TodoPriority.High,
            Timestamp,
            recurrence,
            SeriesId,
            1);

        bool changed = todo.ChangeStatus(TodoStatus.Completed, Timestamp.AddDays(20));

        changed.Should().BeTrue();
        TodoCompletion completion = todo.Completion.Should().NotBeNull().And.Subject
            .Should().BeOfType<TodoCompletion>().Subject;
        completion.TodoId.Should().Be(todo.Id);
        completion.SeriesId.Should().Be(SeriesId);
        completion.OccurrenceNumber.Should().Be(1);
        completion.NextOccurrenceId.Should().NotBeNull().And.NotBe(Guid.Empty);
        completion.ScheduledDueDate.Should().Be(DueDate);
        completion.Recurrence.Should().Be(recurrence);
    }

    [TestMethod]
    public void CompletedToCompletedDoesNotRecordAnotherCompletion()
    {
        TodoItem todo = TodoItem.Create(
            TodoId,
            OwnerId,
            "Submit report",
            null,
            DueDate,
            TodoPriority.High,
            Timestamp);
        _ = todo.ChangeStatus(TodoStatus.Completed, Timestamp.AddHours(1));
        TodoCompletion? first = todo.Completion;

        bool changed = todo.ChangeStatus(TodoStatus.Completed, Timestamp.AddHours(2));

        changed.Should().BeFalse();
        first.Should().NotBeNull();
        todo.Completion.Should().BeSameAs(first);
    }

    /// <summary>
    /// The completion is state on the aggregate rather than a queue something
    /// drains, so a later transition has to clear it. Otherwise a TODO that was
    /// completed and then reopened would still report a completion, and the
    /// status handler would write a second successor for it.
    /// </summary>
    [TestMethod]
    public void MovingOutOfCompletedClearsTheRecordedCompletion()
    {
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            DueDate);
        TodoItem todo = TodoItem.Create(
            TodoId,
            OwnerId,
            "Submit report",
            null,
            DueDate,
            TodoPriority.High,
            Timestamp,
            recurrence,
            SeriesId,
            1);
        _ = todo.ChangeStatus(TodoStatus.Completed, Timestamp.AddHours(1));
        todo.Completion.Should().NotBeNull();

        bool changed = todo.ChangeStatus(TodoStatus.Open, Timestamp.AddHours(2));

        changed.Should().BeTrue();
        todo.Completion.Should().BeNull();
    }
}
