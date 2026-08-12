using FluentAssertions;

using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Tests.ValueObjects;

[TestClass]
public sealed class RecurrenceScheduleTests
{
    private static readonly DateOnly DueDate = new DateOnly(2026, 1, 31);

    [TestMethod]
    [DataRow(RecurrenceType.Daily, RecurrenceUnit.Days, null)]
    [DataRow(RecurrenceType.Weekly, RecurrenceUnit.Weeks, null)]
    [DataRow(RecurrenceType.Monthly, RecurrenceUnit.Months, 31)]
    public void StandardSchedulesUseTheirBusinessUnit(
        RecurrenceType type,
        RecurrenceUnit expectedUnit,
        int? expectedAnchorDay)
    {
        RecurrenceSchedule schedule = RecurrenceSchedule.Create(type, 1, null, DueDate);

        schedule.Type.Should().Be(type);
        schedule.Interval.Should().Be(1);
        schedule.Unit.Should().Be(expectedUnit);
        schedule.AnchorDay.Should().Be(expectedAnchorDay);
    }

    [TestMethod]
    [DataRow(RecurrenceUnit.Days)]
    [DataRow(RecurrenceUnit.Weeks)]
    [DataRow(RecurrenceUnit.Months)]
    public void CustomScheduleSupportsEveryPositiveInterval(RecurrenceUnit unit)
    {
        RecurrenceSchedule schedule = RecurrenceSchedule.Create(
            RecurrenceType.Custom,
            3,
            unit,
            DueDate);

        schedule.Interval.Should().Be(3);
        schedule.Unit.Should().Be(unit);
        schedule.AnchorDay.Should().Be(unit == RecurrenceUnit.Months ? 31 : null);
    }

    [TestMethod]
    public void InvalidSchedulesAreRejected()
    {
        Action zeroInterval = () => RecurrenceSchedule.Create(
            RecurrenceType.Custom,
            0,
            RecurrenceUnit.Days,
            DueDate);
        Action missingCustomUnit = () => RecurrenceSchedule.Create(
            RecurrenceType.Custom,
            1,
            null,
            DueDate);
        Action nonStandardInterval = () => RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            2,
            null,
            DueDate);
        Action mismatchedUnit = () => RecurrenceSchedule.Create(
            RecurrenceType.Daily,
            1,
            RecurrenceUnit.Months,
            DueDate);

        zeroInterval.Should().Throw<DomainException>();
        missingCustomUnit.Should().Throw<DomainException>();
        nonStandardInterval.Should().Throw<DomainException>();
        mismatchedUnit.Should().Throw<DomainException>();
    }
}
