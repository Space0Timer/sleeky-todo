using FluentAssertions;

using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Services;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Domain.Tests.Services;

[TestClass]
public sealed class RecurrenceCalculatorTests
{
    private readonly RecurrenceCalculator calculator = new RecurrenceCalculator();

    [TestMethod]
    public void DailyWeeklyAndCustomSchedulesAdvanceFromScheduledDueDate()
    {
        DateOnly dueDate = new DateOnly(2026, 8, 12);
        RecurrenceSchedule daily = RecurrenceSchedule.Create(
            RecurrenceType.Daily,
            1,
            null,
            dueDate);
        RecurrenceSchedule weekly = RecurrenceSchedule.Create(
            RecurrenceType.Weekly,
            1,
            null,
            dueDate);
        RecurrenceSchedule everyThreeWeeks = RecurrenceSchedule.Create(
            RecurrenceType.Custom,
            3,
            RecurrenceUnit.Weeks,
            dueDate);
        RecurrenceSchedule everyFourDays = RecurrenceSchedule.Create(
            RecurrenceType.Custom,
            4,
            RecurrenceUnit.Days,
            dueDate);

        calculator.CalculateNext(dueDate, daily)
            .Should().Be(new DateOnly(2026, 8, 13));
        calculator.CalculateNext(dueDate, weekly)
            .Should().Be(new DateOnly(2026, 8, 19));
        calculator.CalculateNext(dueDate, everyThreeWeeks)
            .Should().Be(new DateOnly(2026, 9, 2));
        calculator.CalculateNext(dueDate, everyFourDays)
            .Should().Be(new DateOnly(2026, 8, 16));
    }

    [TestMethod]
    public void MonthlySchedulePreservesEndOfMonthAnchor()
    {
        DateOnly january = new DateOnly(2026, 1, 31);
        RecurrenceSchedule monthly = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            january);

        DateOnly february = calculator.CalculateNext(january, monthly);
        DateOnly march = calculator.CalculateNext(february, monthly);

        february.Should().Be(new DateOnly(2026, 2, 28));
        march.Should().Be(new DateOnly(2026, 3, 31));
    }

    [TestMethod]
    public void MonthlyScheduleUsesLeapDayWhenAvailable()
    {
        DateOnly january = new DateOnly(2024, 1, 31);
        RecurrenceSchedule monthly = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            january);

        DateOnly february = calculator.CalculateNext(january, monthly);

        february.Should().Be(new DateOnly(2024, 2, 29));
    }

    [TestMethod]
    public void CustomMonthIntervalPreservesAnchorAcrossShortMonths()
    {
        DateOnly august = new DateOnly(2026, 8, 31);
        RecurrenceSchedule everySixMonths = RecurrenceSchedule.Create(
            RecurrenceType.Custom,
            6,
            RecurrenceUnit.Months,
            august);

        DateOnly february = calculator.CalculateNext(august, everySixMonths);
        DateOnly nextAugust = calculator.CalculateNext(february, everySixMonths);

        february.Should().Be(new DateOnly(2027, 2, 28));
        nextAugust.Should().Be(new DateOnly(2027, 8, 31));
    }

    [TestMethod]
    public void AnnualLeapDayScheduleReturnsToLeapDay()
    {
        DateOnly leapDay = new DateOnly(2024, 2, 29);
        RecurrenceSchedule annual = RecurrenceSchedule.Create(
            RecurrenceType.Custom,
            12,
            RecurrenceUnit.Months,
            leapDay);

        DateOnly next = leapDay;
        for (int year = 2025; year <= 2028; year++)
        {
            next = calculator.CalculateNext(next, annual);
        }

        next.Should().Be(new DateOnly(2028, 2, 29));
    }
}
