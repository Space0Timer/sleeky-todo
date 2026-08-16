using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Commands.Bulk;

namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// What a read hands the model. The version is included because every write
/// binds versions from what was last read, and the enum-valued fields are names
/// rather than the numbers the HTTP contract uses: a model reasons about
/// "Completed", not about 2.
/// </summary>
/// <param name="IsBlocked">
/// Absent from a selection read, which reports stored state rather than the
/// dependency evaluation a list performs. Null therefore means unreported, not
/// unblocked.
/// </param>
public sealed record TodoSummary(
    Guid Id,
    string Name,
    long Version,
    DateOnly DueDate,
    string Status,
    string Priority,
    bool IsDeleted,
    bool? IsBlocked)
{
    public static TodoSummary FromListItem(TodoListItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new TodoSummary(
            item.Id,
            item.Name,
            item.Version,
            item.DueDate,
            item.Status.ToString(),
            item.Priority.ToString(),
            item.DeletedAt is not null,
            item.IsBlocked);
    }

    public static TodoSummary FromTodo(TodoDto todo)
    {
        ArgumentNullException.ThrowIfNull(todo);

        return new TodoSummary(
            todo.Id,
            todo.Name,
            todo.Version,
            todo.DueDate,
            todo.Status.ToString(),
            todo.Priority.ToString(),
            todo.DeletedAt is not null,
            IsBlocked: null);
    }

    /// <summary>
    /// What a bulk write reports about each item: identifier, version, status
    /// and deletion state, and nothing else. The fields a write does not report
    /// are left blank rather than read again, because the write was bound to a
    /// read the model still has in front of it.
    /// </summary>
    public static TodoSummary FromWriteResult(BulkTodoResultItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new TodoSummary(
            item.Id,
            Name: string.Empty,
            item.Version,
            DueDate: default,
            item.Status.ToString(),
            Priority: string.Empty,
            item.DeletedAt is not null,
            IsBlocked: null);
    }
}
