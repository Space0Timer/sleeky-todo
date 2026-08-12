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

    public static TodoItem Create(string id = "todo-1")
    {
        return TodoItem.Create(
            id,
            "Submit report",
            "Monthly report",
            DueDate,
            TodoPriority.High,
            Timestamp);
    }

    public static TodoItem CreateDeleted(string id = "todo-1")
    {
        TodoItem todoItem = Create(id);
        todoItem.SoftDelete(Timestamp.AddDays(1));
        return todoItem;
    }
}
