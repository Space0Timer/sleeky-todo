using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.DeleteTodo;

public sealed record DeleteTodoCommand(
    Guid SpaceId,
    Guid Id,
    long Version) : IRequest<TodoDto>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Write;
}
