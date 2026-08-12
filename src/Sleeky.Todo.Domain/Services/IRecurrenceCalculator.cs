using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Services;

public interface IRecurrenceCalculator
{
    DateOnly CalculateNext(
        DateOnly scheduledDueDate,
        RecurrenceSchedule recurrence);
}
