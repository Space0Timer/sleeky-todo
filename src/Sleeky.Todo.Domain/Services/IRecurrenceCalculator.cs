using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Services;

/// <summary>
/// Advances a recurring TODO's due date by one step of its schedule.
/// </summary>
/// <remarks>
/// The seam the Application layer's occurrence factory takes its date rules
/// through. <see cref="RecurrenceCalculator"/> is the one implementation and
/// documents the calendar rules; tests use it directly rather than a stand-in,
/// because the rules are the behaviour worth checking.
/// </remarks>
public interface IRecurrenceCalculator
{
    /// <summary>
    /// Returns the due date of the occurrence that follows the one scheduled for
    /// <paramref name="scheduledDueDate"/>.
    /// </summary>
    DateOnly CalculateNext(
        DateOnly scheduledDueDate,
        RecurrenceSchedule recurrence);
}
