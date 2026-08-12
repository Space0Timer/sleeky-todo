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

    [TestMethod]
    public void CreateSetsDefaultsAndNormalizesValues()
    {
        TodoItem item = CreateTodo(
            id: "  todo-1  ",
            name: "  Submit Report  ",
            description: "  Monthly report  ");

        item.Id.Should().Be("todo-1");
        item.Name.Should().Be("Submit Report");
        item.NameNormalized.Should().Be("submit report");
        item.Description.Should().Be("Monthly report");
        item.DueDate.Should().Be(InitialDueDate);
        item.Status.Should().Be(TodoStatus.NotStarted);
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
        Func<TodoItem> act = () => CreateTodo(id: "   ");

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
    public void CreateRejectsInvalidPriority()
    {
        Func<TodoItem> act = () => TodoItem.Create(
            "todo-1",
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
    public void AddAndRemoveDependencyUseControlledCollection()
    {
        TodoItem item = CreateTodo();
        DateTimeOffset addedAt = InitialTimestamp.AddHours(1);
        DateTimeOffset removedAt = InitialTimestamp.AddHours(2);

        item.AddDependency("dependency-1", addedAt);

        item.DependencyIds.Should().Equal("dependency-1");
        item.UpdatedAt.Should().Be(addedAt.ToUniversalTime());
        ICollection<string> exposedCollection = (ICollection<string>)item.DependencyIds;
        Action mutateDirectly = () => exposedCollection.Add("dependency-2");
        mutateDirectly.Should().Throw<NotSupportedException>();

        item.RemoveDependency("dependency-1", removedAt);
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

        item.AddDependency("dependency-1", InitialTimestamp);
        Action addDuplicate = () => item.AddDependency(
            "dependency-1",
            InitialTimestamp.AddHours(1));
        addDuplicate.Should()
            .Throw<DomainException>()
            .WithMessage("The TODO dependency already exists.");
    }

    [TestMethod]
    public void RemoveDependencyRejectsMissingDependency()
    {
        TodoItem item = CreateTodo();

        Action act = () => item.RemoveDependency("missing", InitialTimestamp);

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
            "todo-1",
            "Submit Report",
            "Monthly report",
            InitialDueDate,
            TodoStatus.Archived,
            TodoPriority.Medium,
            new[] { "todo-a", "todo-b" },
            recurrence,
            "series-1",
            3,
            7,
            InitialTimestamp,
            updatedAt,
            deletedAt,
            purgeAt);

        item.Id.Should().Be("todo-1");
        item.NameNormalized.Should().Be("submit report");
        item.Status.Should().Be(TodoStatus.Archived);
        item.Priority.Should().Be(TodoPriority.Medium);
        item.DependencyIds.Should().Equal("todo-a", "todo-b");
        item.Recurrence.Should().Be(recurrence);
        item.SeriesId.Should().Be("series-1");
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
            "todo-1",
            "Submit Report",
            null,
            InitialDueDate,
            TodoStatus.NotStarted,
            TodoPriority.Low,
            Array.Empty<string>(),
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
        string id = "todo-1",
        string name = "Submit Report",
        string? description = "Monthly report")
    {
        return TodoItem.Create(
            id,
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
            "todo-1",
            "Submit Report",
            null,
            InitialDueDate,
            TodoStatus.NotStarted,
            TodoPriority.Low,
            Array.Empty<string>(),
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
