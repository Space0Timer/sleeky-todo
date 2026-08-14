using Sleeky.Todo.Application.Abstractions.Events;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Todos.Recurrence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Events;

namespace Sleeky.Todo.Application.Todos.Events;

public sealed class CreateNextRecurringOccurrenceHandler
    : IDomainEventHandler<TodoCompletedDomainEvent>
{
    private readonly IRecurringOccurrenceFactory recurringOccurrenceFactory;
    private readonly ITodoRepository todoRepository;

    public CreateNextRecurringOccurrenceHandler(
        ITodoRepository todoRepository,
        IRecurringOccurrenceFactory recurringOccurrenceFactory)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(recurringOccurrenceFactory);

        this.todoRepository = todoRepository;
        this.recurringOccurrenceFactory = recurringOccurrenceFactory;
    }

    public async Task HandleAsync(
        TodoCompletedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        if (domainEvent.CompletionContext.Recurrence is null)
        {
            return;
        }

        TodoItem nextOccurrence = recurringOccurrenceFactory.CreateNext(domainEvent);

        await todoRepository.AddAsync(nextOccurrence, cancellationToken);
    }
}
