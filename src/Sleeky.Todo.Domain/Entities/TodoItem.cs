using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Events;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Entities;

public sealed class TodoItem
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);

    private readonly List<Guid> dependencyIds = new List<Guid>();
    private readonly List<IDomainEvent> domainEvents = new List<IDomainEvent>();

    private TodoItem(
        Guid id,
        Guid ownerId,
        string name,
        string? description,
        DateOnly dueDate,
        TodoPriority priority,
        DateTimeOffset createdAt)
    {
        Id = id;
        OwnerId = ownerId;
        SetName(name);
        Description = NormalizeDescription(description);
        DueDate = dueDate;
        Status = TodoStatus.NotStarted;
        Priority = ValidatePriority(priority);
        Version = 1;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid OwnerId { get; }

    public string Name { get; private set; } = string.Empty;

    public string NameNormalized { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateOnly DueDate { get; private set; }

    public TodoStatus Status { get; private set; }

    public TodoPriority Priority { get; private set; }

    public IReadOnlyCollection<Guid> DependencyIds => dependencyIds.AsReadOnly();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => domainEvents.AsReadOnly();

    public RecurrenceSchedule? Recurrence { get; private set; }

    public Guid? SeriesId { get; private set; }

    public int? OccurrenceNumber { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset? PurgeAt { get; private set; }

    public static TodoItem Create(
        Guid id,
        Guid ownerId,
        string name,
        string? description,
        DateOnly dueDate,
        TodoPriority priority,
        DateTimeOffset createdAt,
        RecurrenceSchedule? recurrence = null,
        Guid? seriesId = null,
        int? occurrenceNumber = null)
    {
        Guid validatedId = ValidateId(id);
        Guid validatedOwnerId = ValidateOwnerId(ownerId);
        DateTimeOffset utcCreatedAt = createdAt.ToUniversalTime();

        ValidateRecurrenceState(recurrence, seriesId, occurrenceNumber);

        return new TodoItem(
            validatedId,
            validatedOwnerId,
            name,
            description,
            dueDate,
            priority,
            utcCreatedAt)
        {
            Recurrence = recurrence,
            SeriesId = seriesId,
            OccurrenceNumber = occurrenceNumber,
        };
    }

    public static TodoItem Rehydrate(
        Guid id,
        Guid ownerId,
        string name,
        string? description,
        DateOnly dueDate,
        TodoStatus status,
        TodoPriority priority,
        IEnumerable<Guid> dependencyIds,
        RecurrenceSchedule? recurrence,
        Guid? seriesId,
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

        ValidateDeletionState(deletedAt, purgeAt);
        ValidateRecurrenceState(recurrence, seriesId, occurrenceNumber);

        TodoItem todoItem = new TodoItem(
            ValidateId(id),
            ValidateOwnerId(ownerId),
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
        EnsureNotArchived();

        SetName(name);
        Description = NormalizeDescription(description);
        DueDate = dueDate;
        Priority = ValidatePriority(priority);
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public void AddDependency(Guid dependencyId, DateTimeOffset updatedAt)
    {
        EnsureNotDeleted();
        EnsureNotArchived();

        Guid validatedDependencyId = ValidateId(dependencyId);
        if (Id == validatedDependencyId)
        {
            throw new DomainException("A TODO cannot depend on itself.");
        }

        if (dependencyIds.Contains(validatedDependencyId))
        {
            throw new DomainException("The TODO dependency already exists.");
        }

        dependencyIds.Add(validatedDependencyId);
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public void RemoveDependency(Guid dependencyId, DateTimeOffset updatedAt)
    {
        EnsureNotDeleted();
        EnsureNotArchived();

        Guid validatedDependencyId = ValidateId(dependencyId);
        if (!dependencyIds.Remove(validatedDependencyId))
        {
            throw new DomainException("The TODO dependency does not exist.");
        }

        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public bool ChangeStatus(TodoStatus status, DateTimeOffset updatedAt)
    {
        EnsureNotDeleted();

        if (!Enum.IsDefined(status))
        {
            throw new DomainException("A valid TODO status is required.");
        }

        if (Status == status)
        {
            return false;
        }

        if (Status == TodoStatus.Archived && status == TodoStatus.Completed)
        {
            throw new DomainException(
                "An archived TODO must be unarchived before it can be completed.");
        }

        TodoStatus previousStatus = Status;
        DateTimeOffset utcUpdatedAt = updatedAt.ToUniversalTime();
        Status = status;
        UpdatedAt = utcUpdatedAt;

        if (previousStatus != TodoStatus.Completed && status == TodoStatus.Completed)
        {
            Guid? nextOccurrenceId = Recurrence is null
                ? null
                : Guid.NewGuid();
            domainEvents.Add(
                new TodoCompletedDomainEvent(
                    Id,
                    SeriesId,
                    OccurrenceNumber,
                    nextOccurrenceId,
                    new TodoCompletionContext(
                        OwnerId,
                        Name,
                        Description,
                        DueDate,
                        Priority,
                        Recurrence,
                        utcUpdatedAt)));
        }

        return true;
    }

    public void ClearDomainEvents()
    {
        domainEvents.Clear();
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

        if (utcRestoredAt < DeletedAt.Value)
        {
            throw new DomainException("A TODO cannot be restored before it was deleted.");
        }

        if (utcRestoredAt >= PurgeAt.Value)
        {
            throw new DomainException("The TODO retention period has expired.");
        }

        DeletedAt = null;
        PurgeAt = null;
        UpdatedAt = utcRestoredAt;
    }

    private static Guid ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("A TODO identifier is required.");
        }

        return id;
    }

    private static Guid ValidateOwnerId(Guid ownerId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new DomainException("A TODO owner identifier is required.");
        }

        return ownerId;
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

    private static void ValidateDeletionState(
        DateTimeOffset? deletedAt,
        DateTimeOffset? purgeAt)
    {
        if (deletedAt.HasValue != purgeAt.HasValue)
        {
            throw new DomainException(
                "TODO deletion and purge timestamps must either both be set or both be null.");
        }

        if (deletedAt.HasValue && purgeAt <= deletedAt)
        {
            throw new DomainException(
                "A TODO purge timestamp must be later than its deletion timestamp.");
        }
    }

    private static void ValidateRecurrenceState(
        RecurrenceSchedule? recurrence,
        Guid? seriesId,
        int? occurrenceNumber)
    {
        if (recurrence is null)
        {
            ValidateAbsentRecurrenceMetadata(seriesId, occurrenceNumber);
            return;
        }

        if (seriesId is null || seriesId == Guid.Empty)
        {
            throw new DomainException("A recurring TODO requires a series identifier.");
        }

        if (occurrenceNumber is null or <= 0)
        {
            throw new DomainException(
                "A recurring TODO requires a positive occurrence number.");
        }
    }

    private static void ValidateAbsentRecurrenceMetadata(
        Guid? seriesId,
        int? occurrenceNumber)
    {
        if (seriesId is null && !occurrenceNumber.HasValue)
        {
            return;
        }

        throw new DomainException(
            "A non-recurring TODO cannot belong to a recurrence series.");
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

    private void EnsureNotArchived()
    {
        if (Status == TodoStatus.Archived)
        {
            throw new DomainException("An archived TODO cannot be changed.");
        }
    }

    private void SetName(string name)
    {
        Name = ValidateName(name);
        NameNormalized = Name.ToLowerInvariant();
    }
}
