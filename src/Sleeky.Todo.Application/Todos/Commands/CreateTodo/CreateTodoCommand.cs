using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.CreateTodo;

public sealed class CreateTodoCommand : IRequest<TodoDto>
{
    public CreateTodoCommand(
        string name,
        string? description,
        DateOnly dueDate,
        TodoPriority priority,
        RecurrenceType? recurrenceType = null,
        int? recurrenceInterval = null,
        RecurrenceUnit? recurrenceUnit = null)
    {
        Name = name;
        Description = description;
        DueDate = dueDate;
        Priority = priority;
        RecurrenceType = recurrenceType;
        RecurrenceInterval = recurrenceInterval;
        RecurrenceUnit = recurrenceUnit;
    }

    public string Name { get; }

    public string? Description { get; }

    public DateOnly DueDate { get; }

    public TodoPriority Priority { get; }

    public RecurrenceType? RecurrenceType { get; }

    public int? RecurrenceInterval { get; }

    public RecurrenceUnit? RecurrenceUnit { get; }
}
