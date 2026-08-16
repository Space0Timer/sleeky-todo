using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Services;

/// <summary>
/// Advances a recurring TODO's due date by one step of its schedule.
/// </summary>
/// <remarks>
/// The step is taken from the <em>scheduled</em> due date, never from when
/// the TODO was actually completed, so finishing late does not drift the
/// series: a weekly TODO due Monday and completed Thursday is next due the
/// following Monday, not the following Thursday.
///
/// Monthly schedules keep the day the series was anchored on rather than the
/// day the previous occurrence happened to land on. Without that, a series
/// anchored on the 31st would clamp to the 28th in February and stay there
/// for every month after; with it, the series returns to the 31st as soon as a
/// month has one (Jan 31 → Feb 28 → Mar 31).
/// </remarks>
public sealed class RecurrenceCalculator : IRecurrenceCalculator
{
    /// <summary>
    /// Returns the due date of the occurrence that follows the one scheduled for
    /// <paramref name="scheduledDueDate"/>.
    /// </summary>
    /// <param name="scheduledDueDate">
    /// The due date the completed occurrence was scheduled for, not the day it
    /// was completed.
    /// </param>
    /// <param name="recurrence">
    /// The schedule to advance by. A monthly schedule must carry its anchor day,
    /// which <see cref="RecurrenceSchedule.Create"/> guarantees.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="recurrence"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The schedule's unit is not a defined <see cref="RecurrenceUnit"/>.
    /// </exception>
    /// <exception cref="OverflowException">
    /// A weekly interval multiplies past the range of an integer.
    /// </exception>
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

    /// <summary>
    /// Lands on <paramref name="anchorDay"/> of the target month, or on the
    /// month's last day when it is shorter than that.
    /// </summary>
    /// <remarks>
    /// The target month is found by adding whole months first, so the arithmetic
    /// cannot skip a short month; only the day within it is clamped.
    /// </remarks>
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
