namespace Sleeky.Todo.Application.Todos.Commands.Bulk;

/// <summary>
/// One member of a bulk selection: the TODO and the version the client last
/// saw for it, so the whole batch can be refused when any member has moved on.
/// </summary>
public sealed record BulkTodoItemRequest(Guid Id, long Version);
