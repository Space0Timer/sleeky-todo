namespace Sleeky.Todo.Api.Contracts.Todos;

public sealed class BulkTodoSelectionItem
{
    public Guid Id { get; init; }

    public long Version { get; init; }
}
