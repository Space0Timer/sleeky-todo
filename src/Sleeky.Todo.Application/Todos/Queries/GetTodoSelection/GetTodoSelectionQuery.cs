using MediatR;

using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;

public sealed record GetTodoSelectionQuery : IRequest<TodoSelection>, ISpaceScopedRequest
{
    public GetTodoSelectionQuery(Guid spaceId, IReadOnlyCollection<Guid> ids)
    {
        SpaceId = spaceId;
        Ids = ids ?? Array.Empty<Guid>();
    }

    public Guid SpaceId { get; }

    public IReadOnlyCollection<Guid> Ids { get; }

    public SpacePermission RequiredPermission => SpacePermission.Read;
}
