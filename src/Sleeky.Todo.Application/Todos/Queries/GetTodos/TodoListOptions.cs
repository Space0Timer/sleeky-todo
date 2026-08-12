namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

public enum TodoDependencyStatus
{
    Blocked = 0,
    Unblocked = 1,
}

public enum TodoListScope
{
    Active = 0,
    Archived = 1,
    Deleted = 2,
}

public enum TodoSortField
{
    DueDate = 0,
    Priority = 1,
    Status = 2,
    Name = 3,
    NameNormalized = Name,
}

public enum SortDirection
{
    Asc = 0,
    Ascending = Asc,
    Desc = 1,
    Descending = Desc,
}
