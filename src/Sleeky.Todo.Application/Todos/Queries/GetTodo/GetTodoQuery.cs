using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodo;

public sealed class GetTodoQuery : IRequest<TodoDto>
{
    public GetTodoQuery(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }
}
