using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Abstractions.Persistence;

public sealed record TodoListCriteria
{
    public TodoListCriteria(
        TodoStatus? status,
        TodoPriority? priority,
        DateOnly? dueFrom,
        DateOnly? dueTo,
        TodoDependencyStatus? dependencyStatus,
        TodoListScope scope,
        TodoSortField sortField,
        SortDirection sortDirection,
        int limit,
        string? lastSortValue,
        Guid? lastTodoId,
        IReadOnlyList<string> searchTerms)
    {
        ArgumentNullException.ThrowIfNull(searchTerms);

        Status = status;
        Priority = priority;
        DueFrom = dueFrom;
        DueTo = dueTo;
        DependencyStatus = dependencyStatus;
        Scope = scope;
        SortField = sortField;
        SortDirection = sortDirection;
        Limit = limit;
        LastSortValue = lastSortValue;
        LastTodoId = lastTodoId;
        SearchTerms = searchTerms;
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

    public string? LastSortValue { get; }

    public Guid? LastTodoId { get; }

    /// <summary>
    /// The already-tokenized search terms, empty when nothing was searched for.
    /// Each must match a stored token as a prefix, and all of them must match.
    /// </summary>
    /// <remarks>
    /// Splitting happens above this boundary so Infrastructure never learns the
    /// tokenizer's rules and cannot drift from what the write path stored.
    /// </remarks>
    public IReadOnlyList<string> SearchTerms { get; }
}
