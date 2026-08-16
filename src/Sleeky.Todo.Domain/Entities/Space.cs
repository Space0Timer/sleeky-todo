using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Entities;

/// <summary>
/// A shared TODO list: the boundary inside which TODOs live and the access
/// list that says who may see and change them.
/// </summary>
/// <remarks>
/// A Space holds no TODOs; a TODO points at its Space, so this entity is the
/// authorization boundary and nothing more. Its own state is the name and the
/// access list, and only those two are what <see cref="Version"/> protects —
/// a TODO write never touches the Space, so two members editing different
/// TODOs never contend on it.
///
/// Every mutation takes its timestamp from the caller, and the entity never
/// advances <see cref="Version"/>: the repository matches the stored document
/// on identifier and version together and increments it there, exactly as it
/// does for <see cref="TodoItem"/>.
///
/// A rule this entity refuses throws <see cref="DomainException"/>.
/// </remarks>
public sealed class Space
{
    private readonly List<SpaceAccessEntry> access = new List<SpaceAccessEntry>();

    private Space(Guid id, string name, DateTimeOffset createdAt)
    {
        Id = id;
        Name = ValidateName(name);
        Version = 1;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>
    /// The identifier, fixed at creation. A caller supplies it, so a personal
    /// Space can carry an identifier derived from its user and be created
    /// idempotently.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// The trimmed, non-blank display name. Not unique: two Spaces may share
    /// a name, because the identifier is what a route names.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Who may act in this Space and at what level. Every subject appears at
    /// most once, and at least one entry is an <see cref="SpacePermission.Owner"/>.
    /// </summary>
    public IReadOnlyCollection<SpaceAccessEntry> Access => access.AsReadOnly();

    /// <summary>
    /// The optimistic concurrency token for the name and access list,
    /// starting at 1. Never advanced here; see the class remarks.
    /// </summary>
    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a Space whose only member is its creator, as Owner.
    /// </summary>
    /// <exception cref="DomainException">
    /// The identifier, name, or owner identifier is invalid.
    /// </exception>
    public static Space Create(
        Guid id,
        string name,
        Guid ownerUserId,
        DateTimeOffset createdAt)
    {
        Space space = new Space(ValidateId(id), name, createdAt.ToUniversalTime());
        space.access.Add(new SpaceAccessEntry(
            ValidateSubjectId(ownerUserId),
            SubjectType.User,
            SpacePermission.Owner));

        return space;
    }

    /// <summary>
    /// Rebuilds a Space from stored state, re-checking the invariants storage
    /// is expected to hold so a document altered outside the application
    /// fails here rather than becoming an entity creation would never allow.
    /// </summary>
    /// <exception cref="DomainException">
    /// A stored value violates an invariant this entity guarantees.
    /// </exception>
    public static Space Rehydrate(
        Guid id,
        string name,
        IEnumerable<SpaceAccessEntry> access,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(access);

        if (version <= 0)
        {
            throw new DomainException("A positive Space version is required.");
        }

        Space space = new Space(ValidateId(id), name, createdAt.ToUniversalTime())
        {
            Version = version,
            UpdatedAt = updatedAt.ToUniversalTime(),
        };

        foreach (SpaceAccessEntry entry in access)
        {
            space.AddValidatedEntry(entry);
        }

        space.EnsureHasOwner();

        return space;
    }

    /// <exception cref="DomainException">The name is blank.</exception>
    public void Rename(string name, DateTimeOffset updatedAt)
    {
        Name = ValidateName(name);
        Touch(updatedAt);
    }

    /// <summary>
    /// Grants a subject access at the given level.
    /// </summary>
    /// <exception cref="DomainException">
    /// A value is invalid, or the subject already has access.
    /// </exception>
    public void AddAccess(
        Guid subjectId,
        SubjectType subjectType,
        SpacePermission permission,
        DateTimeOffset updatedAt)
    {
        AddValidatedEntry(new SpaceAccessEntry(subjectId, subjectType, permission));
        Touch(updatedAt);
    }

    /// <summary>
    /// Moves an existing subject to a different level.
    /// </summary>
    /// <remarks>
    /// Downgrading the last Owner is refused before anything changes, so the
    /// entity is never observed without an Owner, even transiently.
    /// </remarks>
    /// <exception cref="DomainException">
    /// The permission is invalid, the subject has no access, or the change
    /// would leave the Space without an Owner.
    /// </exception>
    public void ChangePermission(
        Guid subjectId,
        SubjectType subjectType,
        SpacePermission permission,
        DateTimeOffset updatedAt)
    {
        SpacePermission validatedPermission = ValidatePermission(permission);
        int index = RequireIndexOf(subjectId, subjectType);
        SpaceAccessEntry entry = access[index];

        if (entry.Permission == SpacePermission.Owner
            && validatedPermission != SpacePermission.Owner)
        {
            EnsureAnotherOwnerBesides(entry);
        }

        access[index] = entry with { Permission = validatedPermission };
        Touch(updatedAt);
    }

    /// <summary>
    /// Revokes a subject's access.
    /// </summary>
    /// <exception cref="DomainException">
    /// The subject has no access, or it is the last Owner.
    /// </exception>
    public void RemoveAccess(
        Guid subjectId,
        SubjectType subjectType,
        DateTimeOffset updatedAt)
    {
        int index = RequireIndexOf(subjectId, subjectType);
        SpaceAccessEntry entry = access[index];

        if (entry.Permission == SpacePermission.Owner)
        {
            EnsureAnotherOwnerBesides(entry);
        }

        access.RemoveAt(index);
        Touch(updatedAt);
    }

    /// <summary>
    /// The level a subject holds here, or null when it has no access at all.
    /// </summary>
    /// <remarks>
    /// Null rather than a "None" level, so a caller cannot mistake absence for
    /// a grant: the two are answered differently — absence is a 404, an
    /// insufficient level is a 403 — and the type makes the caller decide.
    /// </remarks>
    public SpacePermission? PermissionFor(Guid subjectId, SubjectType subjectType)
    {
        int index = IndexOf(subjectId, subjectType);

        return index < 0 ? null : access[index].Permission;
    }

    private static Guid ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("A Space identifier is required.");
        }

        return id;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A Space name is required.");
        }

        return name.Trim();
    }

    private static Guid ValidateSubjectId(Guid subjectId)
    {
        if (subjectId == Guid.Empty)
        {
            throw new DomainException("A Space access subject identifier is required.");
        }

        return subjectId;
    }

    private static SubjectType ValidateSubjectType(SubjectType subjectType)
    {
        if (!Enum.IsDefined(subjectType))
        {
            throw new DomainException("A valid Space access subject type is required.");
        }

        return subjectType;
    }

    private static SpacePermission ValidatePermission(SpacePermission permission)
    {
        if (!Enum.IsDefined(permission))
        {
            throw new DomainException("A valid Space permission is required.");
        }

        return permission;
    }

    /// <summary>
    /// The single path every entry enters through, from creation, grants, and
    /// rehydration alike, so no path can admit an invalid or duplicate one.
    /// </summary>
    private void AddValidatedEntry(SpaceAccessEntry entry)
    {
        SpaceAccessEntry validated = new SpaceAccessEntry(
            ValidateSubjectId(entry.SubjectId),
            ValidateSubjectType(entry.SubjectType),
            ValidatePermission(entry.Permission));

        if (IndexOf(validated.SubjectId, validated.SubjectType) >= 0)
        {
            throw new DomainException("The subject already has access to the Space.");
        }

        access.Add(validated);
    }

    private void EnsureHasOwner()
    {
        if (!access.Any(entry => entry.Permission == SpacePermission.Owner))
        {
            throw new DomainException("A Space must have at least one Owner.");
        }
    }

    private void EnsureAnotherOwnerBesides(SpaceAccessEntry entry)
    {
        bool anotherOwnerExists = access.Any(other =>
            other != entry && other.Permission == SpacePermission.Owner);

        if (!anotherOwnerExists)
        {
            throw new DomainException("A Space must keep at least one Owner.");
        }
    }

    private int RequireIndexOf(Guid subjectId, SubjectType subjectType)
    {
        int index = IndexOf(subjectId, subjectType);
        if (index < 0)
        {
            throw new DomainException("The subject has no access to the Space.");
        }

        return index;
    }

    private int IndexOf(Guid subjectId, SubjectType subjectType)
    {
        return access.FindIndex(entry =>
            entry.SubjectId == subjectId && entry.SubjectType == subjectType);
    }

    private void Touch(DateTimeOffset updatedAt)
    {
        UpdatedAt = updatedAt.ToUniversalTime();
    }
}
