using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.DTOs;

public sealed class TodoDto
{
    private TodoDto(
        string id,
        string name,
        string? description,
        DateOnly dueDate,
        TodoStatus status,
        TodoPriority priority,
        IReadOnlyCollection<string> dependencyIds,
        RecurrenceSchedule? recurrence,
        string? seriesId,
        int? occurrenceNumber,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? deletedAt,
        DateTimeOffset? purgeAt,
        string? nextOccurrenceId)
    {
        Id = id;
        Name = name;
        Description = description;
        DueDate = dueDate;
        Status = status;
        Priority = priority;
        DependencyIds = dependencyIds;
        Recurrence = recurrence;
        SeriesId = seriesId;
        OccurrenceNumber = occurrenceNumber;
        Version = version;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        DeletedAt = deletedAt;
        PurgeAt = purgeAt;
        NextOccurrenceId = nextOccurrenceId;
    }

    public string Id { get; }

    public string Name { get; }

    public string? Description { get; }

    public DateOnly DueDate { get; }

    public TodoStatus Status { get; }

    public TodoPriority Priority { get; }

    public IReadOnlyCollection<string> DependencyIds { get; }

    public RecurrenceSchedule? Recurrence { get; }

    public string? SeriesId { get; }

    public int? OccurrenceNumber { get; }

    public long Version { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public DateTimeOffset? DeletedAt { get; }

    public DateTimeOffset? PurgeAt { get; }

    public string? NextOccurrenceId { get; }

    public static TodoDto FromEntity(
        TodoItem todoItem,
        string? nextOccurrenceId = null)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        return new TodoDto(
            todoItem.Id,
            todoItem.Name,
            todoItem.Description,
            todoItem.DueDate,
            todoItem.Status,
            todoItem.Priority,
            todoItem.DependencyIds.ToArray(),
            todoItem.Recurrence,
            todoItem.SeriesId,
            todoItem.OccurrenceNumber,
            todoItem.Version,
            todoItem.CreatedAt,
            todoItem.UpdatedAt,
            todoItem.DeletedAt,
            todoItem.PurgeAt,
            nextOccurrenceId);
    }
}
