using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Todos.Recurrence;

public interface IRecurringOccurrenceFactory
{
    TodoItem CreateNext(TodoCompletion completion);
}
