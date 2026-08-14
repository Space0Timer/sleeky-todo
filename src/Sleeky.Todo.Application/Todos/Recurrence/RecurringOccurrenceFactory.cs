using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Events;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.Services;

namespace Sleeky.Todo.Application.Todos.Recurrence;

public sealed class RecurringOccurrenceFactory : IRecurringOccurrenceFactory
{
    private readonly IRecurrenceCalculator recurrenceCalculator;

    public RecurringOccurrenceFactory(IRecurrenceCalculator recurrenceCalculator)
    {
        ArgumentNullException.ThrowIfNull(recurrenceCalculator);

        this.recurrenceCalculator = recurrenceCalculator;
    }

    public TodoItem CreateNext(TodoCompletedDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent.CompletionContext.Recurrence is null)
        {
            throw new DomainException(
                "A next occurrence requires a recurring completion.");
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

        return TodoItem.Create(
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
    }
}
