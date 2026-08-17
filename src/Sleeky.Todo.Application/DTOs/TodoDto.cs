using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.DTOs;

public sealed record TodoDto
{
    private TodoDto(
        Guid id,
        Guid spaceId,
        Guid createdByUserId,
        string name,
        string? description,
        DateOnly dueDate,
        TodoStatus status,
        TodoPriority priority,
        IReadOnlyCollection<Guid> dependencyIds,
        RecurrenceSchedule? recurrence,
        Guid? seriesId,
        int? occurrenceNumber,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? deletedAt,
        DateTimeOffset? purgeAt,
        Guid? nextOccurrenceId)
    {
        Id = id;
        SpaceId = spaceId;
        CreatedByUserId = createdByUserId;
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

    public Guid Id { get; }

    public Guid SpaceId { get; }

    public Guid CreatedByUserId { get; }

    public string Name { get; }

    public string? Description { get; }

    public DateOnly DueDate { get; }

    public TodoStatus Status { get; }

    public TodoPriority Priority { get; }

    public IReadOnlyCollection<Guid> DependencyIds { get; }

    public RecurrenceSchedule? Recurrence { get; }

    public Guid? SeriesId { get; }

    public int? OccurrenceNumber { get; }

    public long Version { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public DateTimeOffset? DeletedAt { get; }

    public DateTimeOffset? PurgeAt { get; }

    public Guid? NextOccurrenceId { get; }

    public static TodoDto FromEntity(
        TodoItem todoItem,
        Guid? nextOccurrenceId = null)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        return new TodoDto(
            todoItem.Id,
            todoItem.SpaceId,
            todoItem.CreatedByUserId,
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
