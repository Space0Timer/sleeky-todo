using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Abstractions.Persistence;

public interface ITodoListReader
{
    Task<IReadOnlyList<TodoListItemDto>> GetTodosAsync(
        TodoListCriteria criteria,
        CancellationToken cancellationToken = default);
}
