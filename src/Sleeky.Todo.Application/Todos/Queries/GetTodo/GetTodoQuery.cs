using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodo;

public sealed record GetTodoQuery(Guid SpaceId, Guid Id) : IRequest<TodoDto>, ISpaceScopedRequest
{
    public SpacePermission RequiredPermission => SpacePermission.Read;
}
