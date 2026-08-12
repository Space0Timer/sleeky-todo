using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Commands.RestoreTodo;

public sealed class RestoreTodoCommand : IRequest<TodoDto>
{
    public RestoreTodoCommand(string id, long version)
    {
        Id = id;
        Version = version;
    }

    public string Id { get; }

    public long Version { get; }
}
