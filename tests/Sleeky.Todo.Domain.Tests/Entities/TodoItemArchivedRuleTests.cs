using FluentAssertions;

using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Domain.Tests.Entities;

/// <summary>
/// An archived TODO is frozen: it accepts no edits and cannot be completed
/// without being unarchived first. Deletion stays available so archived records
/// can still be cleaned up.
/// </summary>
[TestClass]
public sealed class TodoItemArchivedRuleTests
{
    private static readonly DateOnly DueDate = new DateOnly(2026, 8, 31);
    private static readonly DateTimeOffset Timestamp = new DateTimeOffset(
        2026,
        8,
        12,
        9,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public void ArchivedTodoCannotBeCompleted()
    {
        TodoItem item = CreateArchived();

        Action act = () => item.ChangeStatus(TodoStatus.Completed, Timestamp.AddHours(1));

        act.Should()
            .Throw<DomainException>()
            .WithMessage("An archived TODO must be unarchived before it can be completed.");
        item.Status.Should().Be(TodoStatus.Archived);
        item.DomainEvents.Should().BeEmpty();
    }

    [TestMethod]
    public void ArchivedTodoCannotBeEdited()
    {
        TodoItem item = CreateArchived();

        Action act = () => item.UpdateDetails(
            "Renamed",
            null,
            DueDate,
            TodoPriority.Low,
            Timestamp.AddHours(1));

        act.Should()
            .Throw<DomainException>()
            .WithMessage("An archived TODO cannot be changed.");
    }

    [TestMethod]
    public void ArchivedTodoCannotChangeDependencies()
    {
        Guid dependencyId = Guid.NewGuid();
        TodoItem item = CreateActive();
        item.AddDependency(dependencyId, Timestamp);
        _ = item.ChangeStatus(TodoStatus.Archived, Timestamp.AddHours(1));

        Action add = () => item.AddDependency(Guid.NewGuid(), Timestamp.AddHours(2));
        Action remove = () => item.RemoveDependency(dependencyId, Timestamp.AddHours(2));

        add.Should()
            .Throw<DomainException>()
            .WithMessage("An archived TODO cannot be changed.");
        remove.Should()
            .Throw<DomainException>()
            .WithMessage("An archived TODO cannot be changed.");
        item.DependencyIds.Should().Equal(dependencyId);
    }

    [TestMethod]
    [DataRow(TodoStatus.NotStarted)]
    [DataRow(TodoStatus.InProgress)]
    public void ArchivedTodoCanBeUnarchived(TodoStatus status)
    {
        TodoItem item = CreateArchived();

        bool changed = item.ChangeStatus(status, Timestamp.AddHours(1));

        changed.Should().BeTrue();
        item.Status.Should().Be(status);
    }

    [TestMethod]
    public void ArchivedTodoCanStillBeSoftDeleted()
    {
        TodoItem item = CreateArchived();
        DateTimeOffset deletedAt = Timestamp.AddHours(1);

        item.SoftDelete(deletedAt);

        item.DeletedAt.Should().Be(deletedAt);
        item.PurgeAt.Should().Be(deletedAt.AddDays(90));
        item.Status.Should().Be(TodoStatus.Archived);
    }

    [TestMethod]
    [DataRow(TodoStatus.NotStarted)]
    [DataRow(TodoStatus.InProgress)]
    [DataRow(TodoStatus.Completed)]
    public void ArchivingIsAllowedFromEveryOtherStatus(TodoStatus status)
    {
        TodoItem item = CreateActive();
        _ = item.ChangeStatus(status, Timestamp.AddMinutes(1));

        bool changed = item.ChangeStatus(TodoStatus.Archived, Timestamp.AddHours(1));

        changed.Should().BeTrue();
        item.Status.Should().Be(TodoStatus.Archived);
    }

    [TestMethod]
    public void ArchivingAnArchivedTodoIsANoOp()
    {
        TodoItem item = CreateArchived();

        bool changed = item.ChangeStatus(TodoStatus.Archived, Timestamp.AddHours(1));

        changed.Should().BeFalse();
    }

    private static TodoItem CreateActive()
    {
        return TodoItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Submit report",
            "Monthly report",
            DueDate,
            TodoPriority.High,
            Timestamp);
    }

    private static TodoItem CreateArchived()
    {
        TodoItem item = CreateActive();
        _ = item.ChangeStatus(TodoStatus.Archived, Timestamp.AddMinutes(1));
        return item;
    }
}
