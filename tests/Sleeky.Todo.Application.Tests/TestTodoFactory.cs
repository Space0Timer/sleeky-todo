using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests;

internal static class TestTodoFactory
{
    public static readonly DateOnly DueDate = new DateOnly(2026, 8, 31);
    public static readonly DateTimeOffset Timestamp = new DateTimeOffset(
        2026,
        8,
        12,
        9,
        0,
        0,
        TimeSpan.Zero);

    public static readonly Guid SpaceId = CreateId("space-1");
    public static readonly Guid CreatedByUserId = CreateId("user-1");

    public static TodoItem Create(string id = "todo-1")
    {
        return Create(id, SpaceId, CreatedByUserId);
    }

    public static TodoItem Create(string id, Guid spaceId, Guid createdByUserId)
    {
        return TodoItem.Create(
            CreateId(id),
            spaceId,
            createdByUserId,
            "Submit report",
            "Monthly report",
            DueDate,
            TodoPriority.High,
            Timestamp);
    }

    public static Guid CreateId(string value)
    {
        byte[] bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }

    public static TodoItem CreateDeleted(string id = "todo-1")
    {
        TodoItem todoItem = Create(id);
        todoItem.SoftDelete(Timestamp.AddDays(1));
        return todoItem;
    }

    public static TodoItem WithVersion(TodoItem todoItem, long version)
    {
        return TodoItem.Rehydrate(
            todoItem.Id,
            todoItem.SpaceId,
            todoItem.CreatedByUserId,
            todoItem.Name,
            todoItem.Description,
            todoItem.DueDate,
            todoItem.Status,
            todoItem.Priority,
            todoItem.DependencyIds,
            todoItem.Recurrence,
            todoItem.SeriesId,
            todoItem.OccurrenceNumber,
            version,
            todoItem.CreatedAt,
            todoItem.UpdatedAt,
            todoItem.DeletedAt,
            todoItem.PurgeAt);
    }
}
