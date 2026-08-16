using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

public sealed record GetTodosQuery : IRequest<CursorPage<TodoListItemDto>>
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
        string? cursor = null,
        string? searchText = null)
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

        // Trimmed rather than only emptied, unlike the cursor above: a cursor
        // is an opaque token that has to survive byte for byte, while surrounding
        // space in a search box is the user's typing and never part of a term.
        // Trimming here is what lets the length rule and the cursor signature
        // both work on one canonical form.
        SearchText = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
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

    public string? SearchText { get; }
}
