using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Events;

public sealed record TodoCompletionContext
{
    public TodoCompletionContext(
        Guid ownerId,
        string name,
        string? description,
        DateOnly scheduledDueDate,
        TodoPriority priority,
        RecurrenceSchedule? recurrence,
        DateTimeOffset completedAt)
    {
        OwnerId = ownerId;
        Name = name;
        Description = description;
        ScheduledDueDate = scheduledDueDate;
        Priority = priority;
        Recurrence = recurrence;
        CompletedAt = completedAt;
    }

    public Guid OwnerId { get; }

    public string Name { get; }

    public string? Description { get; }

    public DateOnly ScheduledDueDate { get; }

    public TodoPriority Priority { get; }

    public RecurrenceSchedule? Recurrence { get; }

    public DateTimeOffset CompletedAt { get; }
}
