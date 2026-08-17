using MediatR;

using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.RestoreTodos;

public sealed record BulkRestoreTodosCommand(
    Guid SpaceId,
    IReadOnlyCollection<BulkTodoItemRequest> Items) : IRequest<BulkTodoResult>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Write;
}
