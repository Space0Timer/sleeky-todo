using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

/// <summary>
/// One page of a Space's TODO list under a filter, scope, sort, and optional
/// search, resumed from a cursor when one is supplied.
/// </summary>
/// <remarks>
/// The constructor canonicalises its inputs so the validator, the cursor
/// signature, and the reader all see one form: an absent limit becomes the
/// default, and blank cursor or search text becomes null.
/// </remarks>
public sealed record GetTodosQuery : IRequest<CursorPage<TodoListItemDto>>, ISpaceScopedRequest
{
    /// <summary>
    /// The page size when the caller names none.
    /// </summary>
    public const int DefaultPageSize = 50;

    /// <summary>
    /// The largest page a caller may ask for. Public so the assistant's tools
    /// can state the cap in their own checks rather than discover it from a
    /// validation error.
    /// </summary>
    public const int MaximumPageSize = 100;

    public GetTodosQuery(
        Guid spaceId,
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
        SpaceId = spaceId;
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

    public Guid SpaceId { get; }

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

    public SpacePermission RequiredPermission => SpacePermission.Read;
}
