using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Api.Contracts.Todos;

public sealed class RecurrenceRequest
{
    public RecurrenceType Type { get; init; }

    public int Interval { get; init; } = 1;

    public RecurrenceUnit? Unit { get; init; }
}
