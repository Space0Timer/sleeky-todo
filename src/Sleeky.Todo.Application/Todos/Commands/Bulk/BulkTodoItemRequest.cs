namespace Sleeky.Todo.Application.Todos.Commands.Bulk;

public sealed record BulkTodoItemRequest(Guid Id, long Version);
