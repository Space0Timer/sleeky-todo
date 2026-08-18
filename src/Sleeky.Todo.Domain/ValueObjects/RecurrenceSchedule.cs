using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Domain.ValueObjects;

public sealed record RecurrenceSchedule
{
    /// <summary>
    /// The largest interval a custom schedule may carry, in any unit: a year of
    /// days, seven years of weeks, thirty years of months.
    /// </summary>
    /// <remarks>
    /// Without a ceiling an interval in the millions is accepted at creation
    /// and only fails when the successor's date is calculated, which is inside
    /// the completion — every attempt to complete such a TODO would then fail,
    /// and not as a rule but as an arithmetic error.
    /// </remarks>
    public const int MaximumInterval = 365;

    private RecurrenceSchedule(
        RecurrenceType type,
        int interval,
        RecurrenceUnit unit,
        int? anchorDay)
    {
        Type = type;
        Interval = interval;
        Unit = unit;
        AnchorDay = anchorDay;
    }

    public RecurrenceType Type { get; }

    public int Interval { get; }

    public RecurrenceUnit Unit { get; }

    public int? AnchorDay { get; }

    public static RecurrenceSchedule Create(
        RecurrenceType type,
        int interval,
        RecurrenceUnit? unit,
        DateOnly dueDate)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException("A valid recurrence type is required.");
        }

        EnsureIntervalInRange(interval);

        RecurrenceUnit effectiveUnit = type switch
        {
            RecurrenceType.Daily => RecurrenceUnit.Days,
            RecurrenceType.Weekly => RecurrenceUnit.Weeks,
            RecurrenceType.Monthly => RecurrenceUnit.Months,
            RecurrenceType.Custom when unit.HasValue && Enum.IsDefined(unit.Value) =>
                unit.Value,
            RecurrenceType.Custom => throw new DomainException(
                "A valid recurrence unit is required for a custom schedule."),
            _ => throw new DomainException("A valid recurrence type is required."),
        };

        if (type != RecurrenceType.Custom && interval != 1)
        {
            throw new DomainException(
                "Daily, weekly, and monthly recurrence intervals must be one.");
        }

        if (type != RecurrenceType.Custom
            && unit.HasValue
            && unit.Value != effectiveUnit)
        {
            throw new DomainException(
                "The recurrence unit does not match the recurrence type.");
        }

        int? anchorDay = effectiveUnit == RecurrenceUnit.Months ? dueDate.Day : null;
        return new RecurrenceSchedule(type, interval, effectiveUnit, anchorDay);
    }

    public static RecurrenceSchedule Rehydrate(
        RecurrenceType type,
        int interval,
        RecurrenceUnit unit,
        int? anchorDay)
    {
        if (!Enum.IsDefined(type) || !Enum.IsDefined(unit))
        {
            throw new DomainException("A valid recurrence schedule is required.");
        }

        EnsureIntervalInRange(interval);

        if (type != RecurrenceType.Custom && interval != 1)
        {
            throw new DomainException(
                "Daily, weekly, and monthly recurrence intervals must be one.");
        }

        RecurrenceUnit? standardUnit = type switch
        {
            RecurrenceType.Daily => RecurrenceUnit.Days,
            RecurrenceType.Weekly => RecurrenceUnit.Weeks,
            RecurrenceType.Monthly => RecurrenceUnit.Months,
            _ => null,
        };
        if (standardUnit.HasValue && standardUnit.Value != unit)
        {
            throw new DomainException(
                "The recurrence unit does not match the recurrence type.");
        }

        if (unit == RecurrenceUnit.Months
            && (!anchorDay.HasValue || anchorDay is < 1 or > 31))
        {
            throw new DomainException(
                "A monthly recurrence requires an anchor day from 1 through 31.");
        }

        if (unit != RecurrenceUnit.Months && anchorDay.HasValue)
        {
            throw new DomainException(
                "Only a monthly recurrence can have an anchor day.");
        }

        return new RecurrenceSchedule(type, interval, unit, anchorDay);
    }

    private static void EnsureIntervalInRange(int interval)
    {
        if (interval <= 0)
        {
            throw new DomainException("The recurrence interval must be positive.");
        }

        if (interval > MaximumInterval)
        {
            throw new DomainException(
                $"The recurrence interval must not exceed {MaximumInterval}.");
        }
    }
}
