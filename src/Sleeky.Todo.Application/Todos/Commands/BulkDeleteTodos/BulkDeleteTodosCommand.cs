using MediatR;

using Sleeky.Todo.Application.Todos.Commands.Bulk;

namespace Sleeky.Todo.Application.Todos.Commands.BulkDeleteTodos;

public sealed record BulkDeleteTodosCommand(
    IReadOnlyCollection<BulkTodoItemRequest> Items) : IRequest<BulkTodoResult>;
