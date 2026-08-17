using MediatR;

using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.ChangeTodoStatus;

public sealed record BulkChangeTodoStatusCommand(
    Guid SpaceId,
    TodoStatus Status,
    IReadOnlyCollection<BulkTodoItemRequest> Items) : IRequest<BulkTodoResult>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Write;
}
