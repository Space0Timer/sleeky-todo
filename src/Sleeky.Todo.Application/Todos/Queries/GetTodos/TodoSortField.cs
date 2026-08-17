namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

/// <summary>
/// The field a TODO list is ordered by. Each is backed by a Space-scoped index
/// that ends in <c>_id</c>, which is what lets a page resume exactly where the
/// previous one stopped.
/// </summary>
public enum TodoSortField
{
    DueDate = 0,
    Priority = 1,
    Status = 2,

    /// <summary>
    /// Orders by the stored lowercase name, so the sort is case-insensitive
    /// and index-served rather than collated per query.
    /// </summary>
    Name = 3,
}
