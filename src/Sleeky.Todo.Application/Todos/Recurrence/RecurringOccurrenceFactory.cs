using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.Services;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Todos.Recurrence;

public sealed class RecurringOccurrenceFactory : IRecurringOccurrenceFactory
{
    private readonly IRecurrenceCalculator recurrenceCalculator;

    public RecurringOccurrenceFactory(IRecurrenceCalculator recurrenceCalculator)
    {
        ArgumentNullException.ThrowIfNull(recurrenceCalculator);

        this.recurrenceCalculator = recurrenceCalculator;
    }

    public TodoItem CreateNext(TodoCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        if (completion.Recurrence is null)
        {
            throw new DomainException(
                "A next occurrence requires a recurring completion.");
        }

        if (completion.SeriesId is null
            || completion.OccurrenceNumber is null
            || completion.NextOccurrenceId is null)
        {
            throw new DomainException(
                "A recurring completion requires complete series context.");
        }

        DateOnly nextDueDate = recurrenceCalculator.CalculateNext(
            completion.ScheduledDueDate,
            completion.Recurrence);

        // The successor stays in the completed occurrence's Space and keeps its
        // original creator: completing a step does not re-attribute the series
        // to whoever completed it.
        return TodoItem.Create(
            completion.NextOccurrenceId.Value,
            completion.SpaceId,
            completion.CreatedByUserId,
            completion.Name,
            completion.Description,
            nextDueDate,
            completion.Priority,
            completion.CompletedAt,
            completion.Recurrence,
            completion.SeriesId,
            checked(completion.OccurrenceNumber.Value + 1));
    }
}
