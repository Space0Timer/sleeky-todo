using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.RestoreTodo;

public sealed record RestoreTodoCommand(
    Guid SpaceId,
    Guid Id,
    long Version) : IRequest<TodoDto>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Write;
}
