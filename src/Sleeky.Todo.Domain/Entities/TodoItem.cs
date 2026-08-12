using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Entities;

public sealed class TodoItem
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);

    private readonly List<string> dependencyIds = new List<string>();

    private TodoItem(
        string id,
        string name,
        string? description,
        DateOnly dueDate,
        TodoPriority priority,
        DateTimeOffset createdAt)
    {
        Id = id;
        SetName(name);
        Description = NormalizeDescription(description);
        DueDate = dueDate;
        Status = TodoStatus.NotStarted;
        Priority = ValidatePriority(priority);
        Version = 1;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string Id { get; }

    public string Name { get; private set; } = string.Empty;

    public string NameNormalized { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateOnly DueDate { get; private set; }

    public TodoStatus Status { get; private set; }

    public TodoPriority Priority { get; private set; }

    public IReadOnlyCollection<string> DependencyIds => dependencyIds.AsReadOnly();

    public RecurrenceSchedule? Recurrence { get; private set; }

    public string? SeriesId { get; private set; }

    public int? OccurrenceNumber { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset? PurgeAt { get; private set; }

    public static TodoItem Create(
        string id,
        string name,
        string? description,
        DateOnly dueDate,
        TodoPriority priority,
        DateTimeOffset createdAt)
    {
        string validatedId = ValidateId(id);
        DateTimeOffset utcCreatedAt = createdAt.ToUniversalTime();

        return new TodoItem(
            validatedId,
            name,
            description,
            dueDate,
            priority,
            utcCreatedAt);
    }

    public static TodoItem Rehydrate(
        string id,
        string name,
        string? description,
        DateOnly dueDate,
        TodoStatus status,
        TodoPriority priority,
        IEnumerable<string> dependencyIds,
        RecurrenceSchedule? recurrence,
        string? seriesId,
        int? occurrenceNumber,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? deletedAt,
        DateTimeOffset? purgeAt)
    {
        ArgumentNullException.ThrowIfNull(dependencyIds);

        if (!Enum.IsDefined(status))
        {
            throw new DomainException("A valid TODO status is required.");
        }

        if (version <= 0)
        {
            throw new DomainException("A positive TODO version is required.");
        }

        TodoItem todoItem = new TodoItem(
            ValidateId(id),
            name,
            description,
            dueDate,
            priority,
            createdAt.ToUniversalTime())
        {
            Status = status,
            Recurrence = recurrence,
            SeriesId = seriesId,
            OccurrenceNumber = occurrenceNumber,
            Version = version,
            UpdatedAt = updatedAt.ToUniversalTime(),
            DeletedAt = deletedAt?.ToUniversalTime(),
            PurgeAt = purgeAt?.ToUniversalTime(),
        };
        todoItem.dependencyIds.AddRange(dependencyIds);

        return todoItem;
    }

    public void UpdateDetails(
        string name,
        string? description,
        DateOnly dueDate,
        TodoPriority priority,
        DateTimeOffset updatedAt)
    {
        EnsureNotDeleted();

        SetName(name);
        Description = NormalizeDescription(description);
        DueDate = dueDate;
        Priority = ValidatePriority(priority);
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public void SoftDelete(DateTimeOffset deletedAt)
    {
        EnsureNotDeleted();

        DateTimeOffset utcDeletedAt = deletedAt.ToUniversalTime();

        DeletedAt = utcDeletedAt;
        PurgeAt = utcDeletedAt.Add(RetentionPeriod);
        UpdatedAt = utcDeletedAt;
    }

    public void Restore(DateTimeOffset restoredAt)
    {
        if (DeletedAt is null || PurgeAt is null)
        {
            throw new DomainException("Only a deleted TODO can be restored.");
        }

        DateTimeOffset utcRestoredAt = restoredAt.ToUniversalTime();

        if (utcRestoredAt >= PurgeAt.Value)
        {
            throw new DomainException("The TODO retention period has expired.");
        }

        DeletedAt = null;
        PurgeAt = null;
        UpdatedAt = utcRestoredAt;
    }

    private static string ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new DomainException("A TODO identifier is required.");
        }

        return id.Trim();
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A TODO name is required.");
        }

        return name.Trim();
    }

    private static TodoPriority ValidatePriority(TodoPriority priority)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new DomainException("A valid TODO priority is required.");
        }

        return priority;
    }

    private static string? NormalizeDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    private void EnsureNotDeleted()
    {
        if (DeletedAt is not null)
        {
            throw new DomainException("A deleted TODO cannot be changed.");
        }
    }

    private void SetName(string name)
    {
        Name = ValidateName(name);
        NameNormalized = Name.ToLowerInvariant();
    }
}
