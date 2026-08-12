using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Api.Contracts.Todos;

public sealed class UpdateTodoRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public DateOnly DueDate { get; init; }

    public TodoPriority Priority { get; init; }

    public long Version { get; init; }
}
