using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Api.Contracts.Todos;

public sealed class ChangeTodoStatusRequest
{
    public TodoStatus Status { get; init; }

    public long Version { get; init; }
}
