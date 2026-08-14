using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Api.Contracts.Todos;

public sealed class BulkChangeTodoStatusRequest
{
    public TodoStatus Status { get; init; }

    public IReadOnlyCollection<BulkTodoSelectionItem> Items { get; init; } =
        Array.Empty<BulkTodoSelectionItem>();
}
