using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Events;

public sealed record TodoCompletionContext
{
    public TodoCompletionContext(
        string name,
        string? description,
        DateOnly scheduledDueDate,
        TodoPriority priority,
        RecurrenceSchedule? recurrence,
        DateTimeOffset completedAt)
    {
        Name = name;
        Description = description;
        ScheduledDueDate = scheduledDueDate;
        Priority = priority;
        Recurrence = recurrence;
        CompletedAt = completedAt;
    }

    public string Name { get; }

    public string? Description { get; }

    public DateOnly ScheduledDueDate { get; }

    public TodoPriority Priority { get; }

    public RecurrenceSchedule? Recurrence { get; }

    public DateTimeOffset CompletedAt { get; }
}
