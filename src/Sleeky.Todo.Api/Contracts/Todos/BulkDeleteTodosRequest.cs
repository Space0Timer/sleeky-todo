namespace Sleeky.Todo.Api.Contracts.Todos;

public sealed class BulkDeleteTodosRequest
{
    public IReadOnlyCollection<BulkTodoSelectionItem> Items { get; init; } =
        Array.Empty<BulkTodoSelectionItem>();
}
