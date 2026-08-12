using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Abstractions.Persistence;

public interface ITodoListReader
{
    Task<IReadOnlyList<TodoListItemDto>> GetTodosAsync(
        TodoListCriteria criteria,
        CancellationToken cancellationToken = default);
}

public sealed class TodoListCriteria
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
        Guid? lastTodoId)
    {
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
}
