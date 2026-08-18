using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk;

/// <summary>
/// The outcome of a bulk command: one <see cref="BulkTodoResultItem"/> per
/// selected TODO, in the order the selection named them.
/// </summary>
public sealed record BulkTodoResult(IReadOnlyCollection<BulkTodoResultItem> Items)
{
    /// <summary>
    /// Shapes the result after the batch has been persisted. An item in
    /// <paramref name="written"/> reports the version the write produced, which
    /// the entity itself never advances; an item that needed no write echoes
    /// its own. A recurring completion's successor is reported only when it is
    /// among <paramref name="inserted"/>: the completion always names one, but
    /// a reopened occurrence completed again finds its successor already there
    /// and inserts nothing.
    /// </summary>
    public static BulkTodoResult FromEntities(
        IReadOnlyList<TodoItem> todos,
        IReadOnlyCollection<TodoItem> written,
        IReadOnlyCollection<TodoItem>? inserted = null)
    {
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(written);

        HashSet<Guid> writtenIds = written.Select(todoItem => todoItem.Id).ToHashSet();
        HashSet<Guid> insertedIds = (inserted ?? [])
            .Select(todoItem => todoItem.Id)
            .ToHashSet();

        BulkTodoResultItem[] items = todos
            .Select(todoItem => ToItem(
                todoItem,
                writtenIds.Contains(todoItem.Id),
                insertedIds))
            .ToArray();

        return new BulkTodoResult(items);
    }

    private static BulkTodoResultItem ToItem(
        TodoItem todoItem,
        bool written,
        IReadOnlySet<Guid> insertedIds)
    {
        Guid? nextOccurrenceId = todoItem.Completion?.NextOccurrenceId;

        return new BulkTodoResultItem(
            todoItem.Id,
            written ? todoItem.Version + 1 : todoItem.Version,
            todoItem.Status,
            todoItem.DeletedAt,
            nextOccurrenceId is { } id && insertedIds.Contains(id) ? id : null);
    }
}
