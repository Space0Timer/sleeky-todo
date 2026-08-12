using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Commands.DeleteTodo;

public sealed class DeleteTodoCommand : IRequest<TodoDto>
{
    public DeleteTodoCommand(Guid id, long version)
    {
        Id = id;
        Version = version;
    }

    public Guid Id { get; }

    public long Version { get; }
}
