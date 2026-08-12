using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos;

internal static class TodoVersionGuard
{
    public static void EnsureExpectedVersion(TodoItem todoItem, long expectedVersion)
    {
        if (todoItem.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException("TODO", todoItem.Id, expectedVersion);
        }
    }
}
