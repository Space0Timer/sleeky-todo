using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Events;

namespace Sleeky.Todo.Application.Todos.Recurrence;

public interface IRecurringOccurrenceFactory
{
    TodoItem CreateNext(TodoCompletedDomainEvent domainEvent);
}
