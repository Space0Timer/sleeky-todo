using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;

public sealed record GetTodoSelectionQuery : IRequest<TodoSelection>
{
    public GetTodoSelectionQuery(IReadOnlyCollection<Guid> ids)
    {
        Ids = ids ?? Array.Empty<Guid>();
    }

    public IReadOnlyCollection<Guid> Ids { get; }
}
