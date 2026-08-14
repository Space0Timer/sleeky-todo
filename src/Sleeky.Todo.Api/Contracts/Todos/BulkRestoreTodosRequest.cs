namespace Sleeky.Todo.Api.Contracts.Todos;

public sealed class BulkRestoreTodosRequest
{
    public IReadOnlyCollection<BulkTodoSelectionItem> Items { get; init; } =
        Array.Empty<BulkTodoSelectionItem>();
}
