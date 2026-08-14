using MediatR;

using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.BulkChangeTodoStatus;

public sealed record BulkChangeTodoStatusCommand(
    TodoStatus Status,
    IReadOnlyCollection<BulkTodoItemRequest> Items) : IRequest<BulkTodoResult>;
