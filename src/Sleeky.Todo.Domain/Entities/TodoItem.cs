using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.Services;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Entities;

/// <summary>
/// A single TODO and the rules that govern its own state.
/// </summary>
/// <remarks>
/// Every rule decidable from one TODO lives here rather than in a handler, so
/// the single-item and bulk endpoints cannot disagree about what an archived or
/// deleted TODO permits. Rules that need other TODOs — whether a prerequisite is
/// complete, whether a new edge would close a cycle — belong to the Application
/// layer, which has the repository this entity deliberately does not.
///
/// Every mutation takes its timestamp from the caller rather than reading a
/// clock here, so one request decides the instant once and each write it makes
/// shares it. Incoming values are converted to UTC, so a caller's offset cannot
/// reach a stored field.
///
/// A rule this entity refuses throws <see cref="DomainException"/>, which the
/// API pipeline translates into a client error.
/// </remarks>
public sealed class TodoItem
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);

    private readonly List<Guid> dependencyIds = new List<Guid>();

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
        Status = TodoStatus.Open;
        Priority = ValidatePriority(priority);
        Version = 1;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>
    /// The identifier, fixed at creation. A caller may supply it so a retried
    /// create is idempotent rather than duplicating the TODO.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// The user this TODO belongs to, fixed at creation. Ownership is enforced
    /// in the persistence boundary, so every read and write is scoped to it.
    /// </summary>
    public Guid OwnerId { get; }

    /// <summary>
    /// The trimmed, non-blank display name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// The invariant-lowercase <see cref="Name"/>, kept as a stored field so
    /// name ordering is served by an index rather than collated per query.
    /// </summary>
    public string NameNormalized { get; private set; } = string.Empty;

    /// <summary>
    /// Optional free text, trimmed; blank input is stored as null rather than
    /// as an empty string.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// The calendar day this TODO is due — a date, not an instant, so no
    /// timezone can move a deadline to a different day.
    /// </summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>
    /// The current workflow state; changed only through
    /// <see cref="ChangeStatus"/>, which decides what each transition permits.
    /// </summary>
    public TodoStatus Status { get; private set; }

    /// <summary>
    /// The urgency the owner assigned; validated to a defined value.
    /// </summary>
    public TodoPriority Priority { get; private set; }

    /// <summary>
    /// The TODOs this one waits on — its prerequisites, never its dependents.
    /// </summary>
    /// <remarks>
    /// Only the identifiers are held. Whether an edge leaves this TODO blocked
    /// depends on TODOs the entity cannot read, so that question is answered in
    /// the Application layer on each list read rather than stored here, where it
    /// would go stale on every completion or deletion of a prerequisite.
    /// </remarks>
    public IReadOnlyCollection<Guid> DependencyIds => dependencyIds.AsReadOnly();

    /// <summary>
    /// What the most recent status change decided, when that change was a
    /// completion; null otherwise. <see cref="ChangeStatus"/> returns whether
    /// anything changed at all, and this carries the detail a completion
    /// produces — including the successor's identifier for a recurring TODO.
    /// </summary>
    public TodoCompletion? Completion { get; private set; }

    /// <summary>
    /// The searchable words of the name and description.
    /// </summary>
    /// <remarks>
    /// Computed rather than stored, so no creation, rehydration, edit, or
    /// recurring occurrence can persist tokens that disagree with the text
    /// they came from by forgetting to recompute them.
    /// </remarks>
    public IReadOnlyCollection<string> SearchTokens =>
        SearchTokenizer.Tokenize(Name, Description);

    /// <summary>
    /// The schedule that produces a successor when this TODO is completed, or
    /// null when it recurs no further.
    /// </summary>
    public RecurrenceSchedule? Recurrence { get; private set; }

    /// <summary>
    /// Identifies the recurrence series this TODO belongs to; null when it is
    /// not part of one.
    /// </summary>
    /// <remarks>
    /// The three recurrence members move together and are validated as a group:
    /// a schedule requires both a series and a positive occurrence number, and a
    /// TODO without a schedule may carry neither. Storage cannot introduce a
    /// half-set combination, because <see cref="Rehydrate"/> applies the same
    /// check <see cref="Create"/> does.
    /// </remarks>
    public Guid? SeriesId { get; private set; }

    /// <summary>
    /// This TODO's one-based position within its series; null outside a series.
    /// </summary>
    public int? OccurrenceNumber { get; private set; }

    /// <summary>
    /// The optimistic concurrency token, starting at 1.
    /// </summary>
    /// <remarks>
    /// The entity never advances this. A write matches the stored document on
    /// identifier and version together and increments it there, so two writers
    /// that both read version <c>n</c> cannot both succeed; the loser is told to
    /// re-read rather than having its change silently overwrite the winner's.
    /// </remarks>
    public long Version { get; private set; }

    /// <summary>
    /// When this TODO was created, in UTC. Fixed for the entity's lifetime.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When this TODO last changed, in UTC. Every mutation sets it, including
    /// deletion and restoration, so it always reflects the most recent write.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// When this TODO was soft-deleted, or null while it is active.
    /// </summary>
    /// <remarks>
    /// Deletion hides a TODO rather than removing it, so this and
    /// <see cref="PurgeAt"/> are set and cleared as a pair and neither is ever
    /// set alone. Ordinary reads exclude a TODO carrying them; Trash is the one
    /// scope that looks past the filter.
    /// </remarks>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>
    /// When the retention period ends and this TODO stops being restorable.
    /// </summary>
    /// <remarks>
    /// Reaching this instant makes <see cref="Restore"/> refuse; it does not
    /// remove the document. Physical removal is a separate maintenance concern
    /// that no request path performs.
    /// </remarks>
    public DateTimeOffset? PurgeAt { get; private set; }

    /// <summary>
    /// Creates a new TODO, open and at version 1.
    /// </summary>
    /// <remarks>
    /// The recurrence arguments are optional as a group: supply all three to
    /// start or continue a series, or none for a one-off. Any other combination
    /// is refused rather than quietly normalised, because a schedule with no
    /// series would produce successors belonging to nothing.
    /// </remarks>
    /// <exception cref="DomainException">
    /// The identifier, owner, name, or priority is invalid, or the recurrence
    /// arguments are inconsistent.
    /// </exception>
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

    /// <summary>
    /// Rebuilds a TODO from stored state, keeping the status, version, and
    /// timestamps it already had rather than restarting them.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Create"/> because the two answer different
    /// questions: creation decides an opening state, rehydration reproduces one
    /// already decided. It still re-checks the invariants storage is expected to
    /// hold, so a document altered outside the application fails here rather
    /// than becoming an entity in a state creation would never have allowed.
    /// </remarks>
    /// <exception cref="DomainException">
    /// A stored value violates an invariant this entity guarantees.
    /// </exception>
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

    /// <summary>
    /// Replaces the editable details, leaving status, dependencies, and
    /// recurrence untouched.
    /// </summary>
    /// <exception cref="DomainException">
    /// The TODO is deleted or archived, or the name or priority is invalid.
    /// </exception>
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

    /// <summary>
    /// Records that this TODO waits on another.
    /// </summary>
    /// <remarks>
    /// Only the rules visible from one TODO are enforced here: no self-reference
    /// and no duplicate edge. Whether the edge closes a longer cycle needs the
    /// rest of the graph, so the Application layer decides that before calling.
    /// </remarks>
    /// <exception cref="DomainException">
    /// The TODO is deleted or archived, the identifier is empty or this TODO's
    /// own, or the dependency is already recorded.
    /// </exception>
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

    /// <summary>
    /// Drops a prerequisite this TODO no longer waits on.
    /// </summary>
    /// <exception cref="DomainException">
    /// The TODO is deleted or archived, or the dependency is not recorded.
    /// </exception>
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

    /// <summary>
    /// Moves the TODO to <paramref name="status"/>, reporting whether that
    /// changed anything.
    /// </summary>
    /// <remarks>
    /// Asking for the status already held is a no-op rather than an error, so a
    /// repeated request, or a bulk selection where some items are already there,
    /// settles instead of failing. A transition into Completed also builds
    /// <see cref="Completion"/>, which carries what a caller needs to write the
    /// successor of a recurring TODO.
    ///
    /// Dependency rules are not enforced here, because whether prerequisites are
    /// complete cannot be decided from one TODO; the caller checks that first.
    /// Archiving is allowed from any status, but completing an archived TODO is
    /// refused — it must be unarchived first, so nothing leaves the frozen state
    /// and completes in a single step.
    /// </remarks>
    /// <returns>
    /// <c>true</c> when the status changed, <c>false</c> when it was already
    /// <paramref name="status"/>.
    /// </returns>
    /// <exception cref="DomainException">
    /// The TODO is deleted, the status is undefined, or an archived TODO is
    /// being completed.
    /// </exception>
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

        // Assigned on every transition, not only on a completion, so a second
        // change on the same instance cannot leave an earlier completion
        // standing for a status that is no longer Completed.
        Completion = previousStatus != TodoStatus.Completed
            && status == TodoStatus.Completed
            ? BuildCompletion(utcUpdatedAt)
            : null;

        return true;
    }

    /// <summary>
    /// Hides the TODO and starts its retention window.
    /// </summary>
    /// <remarks>
    /// Nothing is removed: the document stays and <see cref="Restore"/> brings
    /// it back until <see cref="PurgeAt"/> passes. Whether other TODOs still
    /// depend on this one is not visible from here, so that is checked before
    /// this is called.
    /// </remarks>
    /// <exception cref="DomainException">The TODO is already deleted.</exception>
    public void SoftDelete(DateTimeOffset deletedAt)
    {
        EnsureNotDeleted();

        DateTimeOffset utcDeletedAt = deletedAt.ToUniversalTime();

        DeletedAt = utcDeletedAt;
        PurgeAt = utcDeletedAt.Add(RetentionPeriod);
        UpdatedAt = utcDeletedAt;
    }

    /// <summary>
    /// Returns a deleted TODO to the active list, keeping the status it had
    /// when deleted.
    /// </summary>
    /// <remarks>
    /// The retention rule is enforced here rather than by physical removal:
    /// once <see cref="PurgeAt"/> has passed the TODO refuses to come back even
    /// though its document still exists, so retention holds whether or not a
    /// purge job ever runs. A restore instant earlier than the deletion is
    /// refused too, since it could only come from a clock the deletion did not
    /// see. No dependency check is made: a restored TODO blocks nothing, and its
    /// own prerequisites are evaluated when it next changes status.
    /// </remarks>
    /// <exception cref="DomainException">
    /// The TODO is not deleted, the restore instant precedes the deletion, or
    /// the retention period has expired.
    /// </exception>
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

    private TodoCompletion BuildCompletion(DateTimeOffset completedAt)
    {
        return new TodoCompletion(
            Id,
            OwnerId,
            Name,
            Description,
            DueDate,
            Priority,
            Recurrence,
            completedAt,
            SeriesId,
            OccurrenceNumber,
            Recurrence is null ? null : Guid.NewGuid());
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
