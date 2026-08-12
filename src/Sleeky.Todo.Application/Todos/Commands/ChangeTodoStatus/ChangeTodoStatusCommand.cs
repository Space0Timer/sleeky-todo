using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;

public sealed class ChangeTodoStatusCommand : IRequest<TodoDto>
{
    public ChangeTodoStatusCommand(Guid id, TodoStatus status, long version)
    {
        Id = id;
        Status = status;
        Version = version;
    }

    public Guid Id { get; }

    public TodoStatus Status { get; }

    public long Version { get; }
}
