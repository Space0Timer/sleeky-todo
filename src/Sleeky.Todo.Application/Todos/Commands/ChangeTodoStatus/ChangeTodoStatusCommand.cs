using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;

public sealed class ChangeTodoStatusCommand : IRequest<TodoDto>
{
    public ChangeTodoStatusCommand(string id, TodoStatus status, long version)
    {
        Id = id;
        Status = status;
        Version = version;
    }

    public string Id { get; }

    public TodoStatus Status { get; }

    public long Version { get; }
}
