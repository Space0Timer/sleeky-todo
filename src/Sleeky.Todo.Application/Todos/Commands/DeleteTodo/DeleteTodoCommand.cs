using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Commands.DeleteTodo;

public sealed class DeleteTodoCommand : IRequest<TodoDto>
{
    public DeleteTodoCommand(string id, long version)
    {
        Id = id;
        Version = version;
    }

    public string Id { get; }

    public long Version { get; }
}
