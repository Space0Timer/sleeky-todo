using FluentAssertions;

using Sleeky.Todo.Application.Todos.Recurrence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Services;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Tests.Todos.Recurrence;

[TestClass]
public sealed class RecurringOccurrenceFactoryTests
{
    private readonly RecurringOccurrenceFactory factory =
        new RecurringOccurrenceFactory(new RecurrenceCalculator());

    /// <summary>
    /// The successor stays in the Space it recurs in and keeps the creator who
    /// started the series. Whoever completes an occurrence — in a shared Space
    /// that is often not the creator — does not become the author of the next.
    /// </summary>
    [TestMethod]
    public void CreateNextKeepsTheSpaceAndTheOriginalCreator()
    {
        TodoItem occurrence = CreateRecurring();
        _ = occurrence.ChangeStatus(TodoStatus.Completed, TestTodoFactory.Timestamp.AddDays(3));
        TodoCompletion completion = occurrence.Completion!;

        TodoItem successor = factory.CreateNext(completion);

        successor.Id.Should().Be(completion.NextOccurrenceId!.Value);
        successor.SpaceId.Should().Be(TestTodoFactory.SpaceId);
        successor.CreatedByUserId.Should().Be(TestTodoFactory.CreatedByUserId);
        successor.SeriesId.Should().Be(occurrence.SeriesId);
        successor.OccurrenceNumber.Should().Be(2);
        successor.DueDate.Should().Be(TestTodoFactory.DueDate.AddMonths(1));
        successor.Status.Should().Be(TodoStatus.Open);
        successor.DependencyIds.Should().BeEmpty();
    }

    private static TodoItem CreateRecurring()
    {
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            TestTodoFactory.DueDate);

        return TodoItem.Create(
            TestTodoFactory.CreateId("todo-1"),
            TestTodoFactory.SpaceId,
            TestTodoFactory.CreatedByUserId,
            "Submit report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.High,
            TestTodoFactory.Timestamp,
            recurrence,
            TestTodoFactory.CreateId("series-1"),
            1);
    }
}
