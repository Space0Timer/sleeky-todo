using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.CreateTodo;

public sealed record CreateTodoCommand : IRequest<TodoDto>, ISpaceScopedRequest
{
    /// <summary>
    /// <paramref name="id"/> lets a caller that may repeat a request choose the
    /// identifier, so a retried creation collides on the unique key instead of
    /// inserting a duplicate. The HTTP contract does not expose it: a browser
    /// has no retry that needs it, and a caller-chosen identifier would let a
    /// caller probe for another Space's TODOs through the duplicate it reports.
    /// </summary>
    public CreateTodoCommand(
        Guid spaceId,
        string name,
        string? description,
        DateOnly dueDate,
        TodoPriority priority,
        RecurrenceType? recurrenceType = null,
        int? recurrenceInterval = null,
        RecurrenceUnit? recurrenceUnit = null,
        Guid? id = null)
    {
        SpaceId = spaceId;
        Id = id;
        Name = name;
        Description = description;
        DueDate = dueDate;
        Priority = priority;
        RecurrenceType = recurrenceType;
        RecurrenceInterval = recurrenceInterval;
        RecurrenceUnit = recurrenceUnit;
    }

    public Guid SpaceId { get; }

    public Guid? Id { get; }

    public string Name { get; }

    public string? Description { get; }

    public DateOnly DueDate { get; }

    public TodoPriority Priority { get; }

    public RecurrenceType? RecurrenceType { get; }

    public int? RecurrenceInterval { get; }

    public RecurrenceUnit? RecurrenceUnit { get; }

    public SpacePermission RequiredPermission => SpacePermission.Write;
}
