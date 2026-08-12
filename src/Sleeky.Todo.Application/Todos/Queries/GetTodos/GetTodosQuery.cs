using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

public sealed class GetTodosQuery : IRequest<CursorPage<TodoListItemDto>>
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;

    public GetTodosQuery(
        TodoStatus? status = null,
        TodoPriority? priority = null,
        DateOnly? dueFrom = null,
        DateOnly? dueTo = null,
        TodoDependencyStatus? dependencyStatus = null,
        TodoListScope scope = TodoListScope.Active,
        TodoSortField sortField = TodoSortField.DueDate,
        SortDirection sortDirection = SortDirection.Asc,
        int? limit = null,
        string? cursor = null)
    {
        Status = status;
        Priority = priority;
        DueFrom = dueFrom;
        DueTo = dueTo;
        DependencyStatus = dependencyStatus;
        Scope = scope;
        SortField = sortField;
        SortDirection = sortDirection;
        Limit = limit ?? DefaultPageSize;
        Cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor;
    }

    public TodoStatus? Status { get; }

    public TodoPriority? Priority { get; }

    public DateOnly? DueFrom { get; }

    public DateOnly? DueTo { get; }

    public TodoDependencyStatus? DependencyStatus { get; }

    public TodoListScope Scope { get; }

    public TodoSortField SortField { get; }

    public SortDirection SortDirection { get; }

    public int Limit { get; }

    public string? Cursor { get; }
}
