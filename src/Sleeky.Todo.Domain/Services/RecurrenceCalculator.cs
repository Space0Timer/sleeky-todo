using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Services;

public sealed class RecurrenceCalculator : IRecurrenceCalculator
{
    public DateOnly CalculateNext(
        DateOnly scheduledDueDate,
        RecurrenceSchedule recurrence)
    {
        ArgumentNullException.ThrowIfNull(recurrence);

        return recurrence.Unit switch
        {
            RecurrenceUnit.Days => scheduledDueDate.AddDays(recurrence.Interval),
            RecurrenceUnit.Weeks => scheduledDueDate.AddDays(
                checked(recurrence.Interval * 7)),
            RecurrenceUnit.Months => CalculateNextMonth(
                scheduledDueDate,
                recurrence.Interval,
                recurrence.AnchorDay!.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(recurrence)),
        };
    }

    private static DateOnly CalculateNextMonth(
        DateOnly scheduledDueDate,
        int interval,
        int anchorDay)
    {
        DateOnly targetMonth = scheduledDueDate.AddMonths(interval);
        int day = Math.Min(
            anchorDay,
            DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month));
        return new DateOnly(targetMonth.Year, targetMonth.Month, day);
    }
}
