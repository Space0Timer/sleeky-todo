using Microsoft.AspNetCore.Mvc;

using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Api.Contracts.Todos;

public sealed class GetTodosRequest
{
    [FromQuery(Name = "status")]
    public TodoStatus? Status { get; init; }

    [FromQuery(Name = "priority")]
    public TodoPriority? Priority { get; init; }

    [FromQuery(Name = "due-from")]
    public DateOnly? DueFrom { get; init; }

    [FromQuery(Name = "due-to")]
    public DateOnly? DueTo { get; init; }

    [FromQuery(Name = "dependencyStatus")]
    public TodoDependencyStatus? DependencyStatus { get; init; }

    [FromQuery(Name = "scope")]
    public TodoListScope Scope { get; init; } = TodoListScope.Active;

    [FromQuery(Name = "sortField")]
    public TodoSortField SortField { get; init; } = TodoSortField.DueDate;

    [FromQuery(Name = "sortDirection")]
    public SortDirection SortDirection { get; init; } = SortDirection.Asc;

    [FromQuery(Name = "limit")]
    public int? Limit { get; init; }

    [FromQuery(Name = "cursor")]
    public string? Cursor { get; init; }
}
