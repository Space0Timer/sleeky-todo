using MediatR;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.DeleteTodos;

public sealed record BulkDeleteTodosCommand(
    IReadOnlyCollection<BulkTodoItemRequest> Items) : IRequest<BulkTodoResult>;
