using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.DTOs;

public sealed class TodoListItemDto
{
    public TodoListItemDto(
        string id,
        string name,
        string? descriptionPreview,
        DateOnly dueDate,
        TodoStatus status,
        TodoPriority priority,
        bool isRecurring,
        bool isBlocked,
        int incompleteDependencyCount,
        long version,
        DateTimeOffset? deletedAt,
        DateTimeOffset? purgeAt)
    {
        Id = id;
        Name = name;
        DescriptionPreview = descriptionPreview;
        DueDate = dueDate;
        Status = status;
        Priority = priority;
        IsRecurring = isRecurring;
        IsBlocked = isBlocked;
        IncompleteDependencyCount = incompleteDependencyCount;
        Version = version;
        DeletedAt = deletedAt;
        PurgeAt = purgeAt;
    }

    public string Id { get; }

    public string Name { get; }

    public string? DescriptionPreview { get; }

    public DateOnly DueDate { get; }

    public TodoStatus Status { get; }

    public TodoPriority Priority { get; }

    public bool IsRecurring { get; }

    public bool IsBlocked { get; }

    public int IncompleteDependencyCount { get; }

    public long Version { get; }

    public DateTimeOffset? DeletedAt { get; }

    public DateTimeOffset? PurgeAt { get; }
}
