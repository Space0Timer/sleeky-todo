using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk;

/// <summary>
/// The outcome for one selected TODO. <c>Version</c> is unchanged for an item
/// that needed no write, <c>DeletedAt</c> is set only by a deletion, and
/// <c>NextOccurrenceId</c> only by a recurring completion.
/// </summary>
public sealed record BulkTodoResultItem(
    Guid Id,
    long Version,
    TodoStatus Status,
    DateTimeOffset? DeletedAt,
    Guid? NextOccurrenceId);
