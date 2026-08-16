namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

public sealed record TodoCursorPayload
{
    public int Version { get; init; }

    public string SortField { get; init; } = string.Empty;

    public string Direction { get; init; } = string.Empty;

    public string LastSortValue { get; init; } = string.Empty;

    public Guid LastTodoId { get; init; }

    public string FilterSignature { get; init; } = string.Empty;
}
