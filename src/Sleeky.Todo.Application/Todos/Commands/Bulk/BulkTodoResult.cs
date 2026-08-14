namespace Sleeky.Todo.Application.Todos.Commands.Bulk;

public sealed record BulkTodoResult(IReadOnlyCollection<BulkTodoResultItem> Items);
