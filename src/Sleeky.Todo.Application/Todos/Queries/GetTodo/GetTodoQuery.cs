using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodo;

public sealed class GetTodoQuery : IRequest<TodoDto>
{
    public GetTodoQuery(string id)
    {
        Id = id;
    }

    public string Id { get; }
}
