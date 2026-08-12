using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.UpdateTodo;

public sealed class UpdateTodoCommand : IRequest<TodoDto>
{
    public UpdateTodoCommand(
        string id,
        string name,
        string? description,
        DateOnly dueDate,
        TodoPriority priority,
        long version)
    {
        Id = id;
        Name = name;
        Description = description;
        DueDate = dueDate;
        Priority = priority;
        Version = version;
    }

    public string Id { get; }

    public string Name { get; }

    public string? Description { get; }

    public DateOnly DueDate { get; }

    public TodoPriority Priority { get; }

    public long Version { get; }
}
