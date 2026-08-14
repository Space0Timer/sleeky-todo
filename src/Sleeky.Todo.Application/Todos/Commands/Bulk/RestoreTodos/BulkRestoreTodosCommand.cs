using MediatR;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.RestoreTodos;

public sealed record BulkRestoreTodosCommand(
    IReadOnlyCollection<BulkTodoItemRequest> Items) : IRequest<BulkTodoResult>;
