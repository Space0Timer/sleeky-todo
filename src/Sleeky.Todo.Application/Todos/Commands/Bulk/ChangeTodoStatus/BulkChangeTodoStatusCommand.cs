using MediatR;

using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.ChangeTodoStatus;

public sealed record BulkChangeTodoStatusCommand(
    TodoStatus Status,
    IReadOnlyCollection<BulkTodoItemRequest> Items) : IRequest<BulkTodoResult>;
