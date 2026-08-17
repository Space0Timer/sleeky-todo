using FluentAssertions;

using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Tests.Entities;

[TestClass]
public sealed class TodoItemTests
{
    private static readonly DateOnly InitialDueDate = new DateOnly(2026, 8, 31);
    private static readonly DateTimeOffset InitialTimestamp = new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly Guid TodoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DependencyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherDependencyId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SeriesId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SpaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid CreatedByUserId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [TestMethod]
    public void CreateSetsDefaultsAndNormalizesValues()
    {
        TodoItem item = CreateTodo(
            id: TodoId,
            name: "  Submit Report  ",
            description: "  Monthly report  ");

        item.Id.Should().Be(TodoId);
        item.SpaceId.Should().Be(SpaceId);
        item.CreatedByUserId.Should().Be(CreatedByUserId);
        item.Name.Should().Be("Submit Report");
        item.NameNormalized.Should().Be("submit report");
        item.Description.Should().Be("Monthly report");
        item.DueDate.Should().Be(InitialDueDate);
        item.Status.Should().Be(TodoStatus.Open);
        item.Priority.Should().Be(TodoPriority.High);
        item.DependencyIds.Should().BeEmpty();
        item.Recurrence.Should().BeNull();
        item.SeriesId.Should().BeNull();
        item.OccurrenceNumber.Should().BeNull();
        item.Version.Should().Be(1);
        item.CreatedAt.Should().Be(InitialTimestamp.ToUniversalTime());
        item.UpdatedAt.Should().Be(InitialTimestamp.ToUniversalTime());
        item.DeletedAt.Should().BeNull();
        item.PurgeAt.Should().BeNull();
    }

    [TestMethod]
    public void CreateConvertsBlankDescriptionToNull()
    {
        TodoItem item = CreateTodo(description: "   ");

        item.Description.Should().BeNull();
    }

    [TestMethod]
    public void CreateRejectsMissingIdentifier()
    {
        Func<TodoItem> act = () => CreateTodo(id: Guid.Empty);

        act.Should()
            .Throw<DomainException>()
            .WithMessage("A TODO identifier is required.");
    }

    [TestMethod]
    public void CreateRejectsMissingName()
    {
        Func<TodoItem> act = () => CreateTodo(name: "   ");

        act.Should()
            .Throw<DomainException>()
            .WithMessage("A TODO name is required.");
    }

    [TestMethod]
    public void CreateRejectsEmptySpace()
    {
        Func<TodoItem> act = () => TodoItem.Create(
            TodoId,
            Guid.Empty,
            CreatedByUserId,
            "Submit Report",
            null,
            InitialDueDate,
            TodoPriority.High,
            InitialTimestamp);

        act.Should()
            .Throw<DomainException>()
            .WithMessage("A TODO Space identifier is required.");
    }

    [TestMethod]
    public void CreateRejectsEmptyCreator()
    {
        Func<TodoItem> act = () => TodoItem.Create(
            TodoId,
            SpaceId,
            Guid.Empty,
            "Submit Report",
            null,
            InitialDueDate,
            TodoPriority.High,
            InitialTimestamp);

        act.Should()
            .Throw<DomainException>()
            .WithMessage("A TODO creator identifier is required.");
    }

    [TestMethod]
    public void CreateRejectsInvalidPriority()
    {
        Func<TodoItem> act = () => TodoItem.Create(
            TodoId,
            SpaceId,
            CreatedByUserId,
            "Submit Report",
            null,
            InitialDueDate,
            (TodoPriority)999,
            InitialTimestamp);

        act.Should()
            .Throw<DomainException>()
            .WithMessage("A valid TODO priority is required.");
    }

    [TestMethod]
    public void UpdateDetailsChangesMutableFieldsAndTimestamp()
    {
        TodoItem item = CreateTodo();
        DateTimeOffset updatedAt = InitialTimestamp.AddHours(3);
        DateOnly updatedDueDate = InitialDueDate.AddDays(2);

        item.UpdateDetails(
            "  Review Report  ",
            "  Revised description  ",
            updatedDueDate,
            TodoPriority.Medium,
            updatedAt);

        item.Name.Should().Be("Review Report");
        item.NameNormalized.Should().Be("review report");
        item.Description.Should().Be("Revised description");
        item.DueDate.Should().Be(updatedDueDate);
        item.Priority.Should().Be(TodoPriority.Medium);
        item.CreatedAt.Should().Be(InitialTimestamp.ToUniversalTime());
        item.UpdatedAt.Should().Be(updatedAt.ToUniversalTime());
        item.Version.Should().Be(1);
    }

    [TestMethod]
    public void SearchTokensSpanTheNameAndTheDescription()
    {
        TodoItem item = CreateTodo(name: "Submit Report", description: "Monthly summary");

        item.SearchTokens.Should().Equal("submit", "report", "monthly", "summary");
    }

    /// <summary>
    /// The tokens are computed from the current text rather than captured at
    /// construction, so an edit cannot leave the stored words describing the
    /// previous name.
    /// </summary>
    [TestMethod]
    public void SearchTokensFollowUpdatedDetails()
    {
        TodoItem item = CreateTodo(name: "Submit Report", description: "Monthly summary");

        item.UpdateDetails(
            "Review Invoice",
            "Quarterly totals",
            InitialDueDate,
            TodoPriority.Low,
            InitialTimestamp.AddHours(1));

        item.SearchTokens.Should().Equal("review", "invoice", "quarterly", "totals");
    }

    [TestMethod]
    public void SearchTokensOmitAnAbsentDescription()
    {
        TodoItem item = CreateTodo(name: "Submit Report", description: null);

        item.SearchTokens.Should().Equal("submit", "report");
    }

    [TestMethod]
    public void AddAndRemoveDependencyUseControlledCollection()
    {
        TodoItem item = CreateTodo();
        DateTimeOffset addedAt = InitialTimestamp.AddHours(1);
        DateTimeOffset removedAt = InitialTimestamp.AddHours(2);

        item.AddDependency(DependencyId, addedAt);

        item.DependencyIds.Should().Equal(DependencyId);
        item.UpdatedAt.Should().Be(addedAt.ToUniversalTime());
        ICollection<Guid> exposedCollection = (ICollection<Guid>)item.DependencyIds;
        Action mutateDirectly = () => exposedCollection.Add(OtherDependencyId);
        mutateDirectly.Should().Throw<NotSupportedException>();

        item.RemoveDependency(DependencyId, removedAt);
        item.DependencyIds.Should().BeEmpty();
        item.UpdatedAt.Should().Be(removedAt.ToUniversalTime());
    }

    [TestMethod]
    public void AddDependencyRejectsSelfAndDuplicate()
    {
        TodoItem item = CreateTodo();
        Action addSelf = () => item.AddDependency(item.Id, InitialTimestamp);

        addSelf.Should()
            .Throw<DomainException>()
            .WithMessage("A TODO cannot depend on itself.");

        item.AddDependency(DependencyId, InitialTimestamp);
        Action addDuplicate = () => item.AddDependency(
            DependencyId,
            InitialTimestamp.AddHours(1));
        addDuplicate.Should()
            .Throw<DomainException>()
            .WithMessage("The TODO dependency already exists.");
    }

    [TestMethod]
    public void RemoveDependencyRejectsMissingDependency()
    {
        TodoItem item = CreateTodo();

        Action act = () => item.RemoveDependency(DependencyId, InitialTimestamp);

        act.Should()
            .Throw<DomainException>()
            .WithMessage("The TODO dependency does not exist.");
    }

    [TestMethod]
    public void ChangeStatusUpdatesOnlyForARealTransition()
    {
        TodoItem item = CreateTodo();
        DateTimeOffset changedAt = InitialTimestamp.AddHours(1);

        bool changed = item.ChangeStatus(TodoStatus.InProgress, changedAt);
        bool changedAgain = item.ChangeStatus(
            TodoStatus.InProgress,
            InitialTimestamp.AddHours(2));

        changed.Should().BeTrue();
        changedAgain.Should().BeFalse();
        item.Status.Should().Be(TodoStatus.InProgress);
        item.UpdatedAt.Should().Be(changedAt.ToUniversalTime());
    }

    [TestMethod]
    public void SoftDeleteSetsRetentionTimestamps()
    {
        TodoItem item = CreateTodo();
        DateTimeOffset deletedAt = InitialTimestamp.AddDays(1);

        item.SoftDelete(deletedAt);

        item.DeletedAt.Should().Be(deletedAt.ToUniversalTime());
        item.PurgeAt.Should().Be(deletedAt.ToUniversalTime().AddDays(90));
        item.UpdatedAt.Should().Be(deletedAt.ToUniversalTime());
        item.Version.Should().Be(1);
    }

    [TestMethod]
    public void DeletedTodoCannotBeUpdatedOrDeletedAgain()
    {
        TodoItem item = CreateTodo();
        item.SoftDelete(InitialTimestamp.AddDays(1));

        Action update = () => item.UpdateDetails(
            "Changed",
            null,
            InitialDueDate,
            TodoPriority.Low,
            InitialTimestamp.AddDays(2));
        Action deleteAgain = () => item.SoftDelete(InitialTimestamp.AddDays(2));

        update.Should().Throw<DomainException>();
        deleteAgain.Should().Throw<DomainException>();
    }

    [TestMethod]
    public void RestoreClearsRetentionTimestamps()
    {
        TodoItem item = CreateTodo();
        DateTimeOffset deletedAt = InitialTimestamp.AddDays(1);
        DateTimeOffset restoredAt = deletedAt.AddDays(30);
        item.SoftDelete(deletedAt);

        item.Restore(restoredAt);

        item.DeletedAt.Should().BeNull();
        item.PurgeAt.Should().BeNull();
        item.UpdatedAt.Should().Be(restoredAt.ToUniversalTime());
        item.Version.Should().Be(1);
    }

    [TestMethod]
    public void RestoreRejectsTodoOutsideRetentionPeriod()
    {
        TodoItem item = CreateTodo();
        DateTimeOffset deletedAt = InitialTimestamp.AddDays(1);
        item.SoftDelete(deletedAt);

        Action act = () => item.Restore(deletedAt.AddDays(90));

        act.Should()
            .Throw<DomainException>()
            .WithMessage("The TODO retention period has expired.");
    }

    [TestMethod]
    public void RestoreRejectsTimestampBeforeDeletion()
    {
        TodoItem item = CreateTodo();
        DateTimeOffset deletedAt = InitialTimestamp.AddDays(1);
        item.SoftDelete(deletedAt);

        Action act = () => item.Restore(deletedAt.AddTicks(-1));

        act.Should()
            .Throw<DomainException>()
            .WithMessage("A TODO cannot be restored before it was deleted.");
    }

    [TestMethod]
    public void RestoreRejectsActiveTodo()
    {
        TodoItem item = CreateTodo();

        Action act = () => item.Restore(InitialTimestamp.AddDays(1));

        act.Should()
            .Throw<DomainException>()
            .WithMessage("Only a deleted TODO can be restored.");
    }

    [TestMethod]
    public void RehydrateRestoresPersistedState()
    {
        DateTimeOffset updatedAt = InitialTimestamp.AddDays(1);
        DateTimeOffset deletedAt = updatedAt.AddDays(1);
        DateTimeOffset purgeAt = deletedAt.AddDays(90);
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            InitialDueDate);

        TodoItem item = TodoItem.Rehydrate(
            TodoId,
            SpaceId,
            CreatedByUserId,
            "Submit Report",
            "Monthly report",
            InitialDueDate,
            TodoStatus.Archived,
            TodoPriority.Medium,
            new[] { DependencyId, OtherDependencyId },
            recurrence,
            SeriesId,
            3,
            7,
            InitialTimestamp,
            updatedAt,
            deletedAt,
            purgeAt);

        item.Id.Should().Be(TodoId);
        item.SpaceId.Should().Be(SpaceId);
        item.CreatedByUserId.Should().Be(CreatedByUserId);
        item.NameNormalized.Should().Be("submit report");
        item.Status.Should().Be(TodoStatus.Archived);
        item.Priority.Should().Be(TodoPriority.Medium);
        item.DependencyIds.Should().Equal(DependencyId, OtherDependencyId);
        item.Recurrence.Should().Be(recurrence);
        item.SeriesId.Should().Be(SeriesId);
        item.OccurrenceNumber.Should().Be(3);
        item.Version.Should().Be(7);
        item.CreatedAt.Should().Be(InitialTimestamp.ToUniversalTime());
        item.UpdatedAt.Should().Be(updatedAt.ToUniversalTime());
        item.DeletedAt.Should().Be(deletedAt.ToUniversalTime());
        item.PurgeAt.Should().Be(purgeAt.ToUniversalTime());
    }

    [TestMethod]
    public void RehydrateRejectsInvalidPersistedVersion()
    {
        Func<TodoItem> act = () => TodoItem.Rehydrate(
            TodoId,
            SpaceId,
            CreatedByUserId,
            "Submit Report",
            null,
            InitialDueDate,
            TodoStatus.Open,
            TodoPriority.Low,
            Array.Empty<Guid>(),
            null,
            null,
            null,
            0,
            InitialTimestamp,
            InitialTimestamp,
            null,
            null);

        act.Should()
            .Throw<DomainException>()
            .WithMessage("A positive TODO version is required.");
    }

    [TestMethod]
    public void RehydrateRejectsIncompleteDeletionState()
    {
        Func<TodoItem> act = () => RehydrateWithDeletionState(
            InitialTimestamp.AddDays(1),
            null);

        act.Should()
            .Throw<DomainException>()
            .WithMessage(
                "TODO deletion and purge timestamps must either both be set or both be null.");
    }

    [TestMethod]
    public void RehydrateRejectsPurgeTimestampAtOrBeforeDeletion()
    {
        DateTimeOffset deletedAt = InitialTimestamp.AddDays(1);
        Func<TodoItem> act = () => RehydrateWithDeletionState(deletedAt, deletedAt);

        act.Should()
            .Throw<DomainException>()
            .WithMessage(
                "A TODO purge timestamp must be later than its deletion timestamp.");
    }

    private static TodoItem CreateTodo(
        Guid? id = null,
        string name = "Submit Report",
        string? description = "Monthly report")
    {
        return TodoItem.Create(
            id ?? TodoId,
            SpaceId,
            CreatedByUserId,
            name,
            description,
            InitialDueDate,
            TodoPriority.High,
            InitialTimestamp);
    }

    private static TodoItem RehydrateWithDeletionState(
        DateTimeOffset? deletedAt,
        DateTimeOffset? purgeAt)
    {
        return TodoItem.Rehydrate(
            TodoId,
            SpaceId,
            CreatedByUserId,
            "Submit Report",
            null,
            InitialDueDate,
            TodoStatus.Open,
            TodoPriority.Low,
            Array.Empty<Guid>(),
            null,
            null,
            null,
            1,
            InitialTimestamp,
            InitialTimestamp,
            deletedAt,
            purgeAt);
    }
}
