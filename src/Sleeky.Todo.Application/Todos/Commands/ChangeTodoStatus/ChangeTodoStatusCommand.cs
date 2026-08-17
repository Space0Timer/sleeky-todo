using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;

public sealed record ChangeTodoStatusCommand(
    Guid SpaceId,
    Guid Id,
    TodoStatus Status,
    long Version) : IRequest<TodoDto>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Write;
}
