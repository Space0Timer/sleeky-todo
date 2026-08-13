using Sleeky.Todo.Application.Abstractions.Events;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Events;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.Services;

namespace Sleeky.Todo.Application.Todos.Events;

public sealed class CreateNextRecurringOccurrenceHandler
    : IDomainEventHandler<TodoCompletedDomainEvent>
{
    private readonly IRecurrenceCalculator recurrenceCalculator;
    private readonly ITodoRepository todoRepository;

    public CreateNextRecurringOccurrenceHandler(
        ITodoRepository todoRepository,
        IRecurrenceCalculator recurrenceCalculator)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(recurrenceCalculator);

        this.todoRepository = todoRepository;
        this.recurrenceCalculator = recurrenceCalculator;
    }

    public async Task HandleAsync(
        TodoCompletedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        if (domainEvent.CompletionContext.Recurrence is null)
        {
            return;
        }

        if (domainEvent.SeriesId is null
            || domainEvent.OccurrenceNumber is null
            || domainEvent.NextOccurrenceId is null)
        {
            throw new DomainException(
                "A recurring completion requires complete series context.");
        }

        DateOnly nextDueDate = recurrenceCalculator.CalculateNext(
            domainEvent.CompletionContext.ScheduledDueDate,
            domainEvent.CompletionContext.Recurrence);
        TodoItem nextOccurrence = TodoItem.Create(
            domainEvent.NextOccurrenceId.Value,
            domainEvent.CompletionContext.OwnerId,
            domainEvent.CompletionContext.Name,
            domainEvent.CompletionContext.Description,
            nextDueDate,
            domainEvent.CompletionContext.Priority,
            domainEvent.CompletionContext.CompletedAt,
            domainEvent.CompletionContext.Recurrence,
            domainEvent.SeriesId,
            checked(domainEvent.OccurrenceNumber.Value + 1));

        await todoRepository.AddAsync(nextOccurrence, cancellationToken);
    }
}
