using FluentValidation;

using Sleeky.Todo.Application.Todos.Validation;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.CreateTodo;

public sealed class CreateTodoCommandValidator : AbstractValidator<CreateTodoCommand>
{
    public CreateTodoCommandValidator()
    {
        RuleFor(command => command.Id!.Value)
            .ValidTodoIdentifier()
            .When(command => command.Id.HasValue);

        RuleFor(command => command.Name)
            .ValidTodoName();

        RuleFor(command => command.Description)
            .MaximumTrimmedDescriptionLength();

        RuleFor(command => command.DueDate)
            .NotEqual(default(DateOnly))
            .WithMessage("A valid TODO due date is required.");

        RuleFor(command => command.Priority)
            .IsInEnum()
            .WithMessage("A valid TODO priority is required.");

        When(command => command.RecurrenceType.HasValue, ConfigureRecurrenceRules);
        When(command => !command.RecurrenceType.HasValue, ConfigureNoRecurrenceRules);
    }

    /// <summary>
    /// A daily, weekly, or monthly schedule implies its unit, so a unit is
    /// optional and only ever restates that implication. Naming a different one
    /// is a contradiction, refused rather than resolved in either's favour.
    /// </summary>
    private static bool HasMatchingStandardUnit(CreateTodoCommand command)
    {
        if (!command.RecurrenceUnit.HasValue)
        {
            return true;
        }

        return command.RecurrenceType switch
        {
            RecurrenceType.Daily => command.RecurrenceUnit == RecurrenceUnit.Days,
            RecurrenceType.Weekly => command.RecurrenceUnit == RecurrenceUnit.Weeks,
            RecurrenceType.Monthly => command.RecurrenceUnit == RecurrenceUnit.Months,
            _ => true,
        };
    }

    /// <summary>
    /// The interval and unit a schedule needs, which differ by type: a custom
    /// schedule supplies both, a daily, weekly, or monthly one is fixed at one
    /// and may only restate the unit its type implies.
    /// </summary>
    private void ConfigureRecurrenceRules()
    {
        RuleFor(command => command.RecurrenceType!.Value)
            .IsInEnum()
            .WithMessage("A valid recurrence type is required.");

        RuleFor(command => command.RecurrenceInterval)
            .NotNull()
            .GreaterThan(0)
            .WithMessage("The recurrence interval must be positive.");

        RuleFor(command => command.RecurrenceUnit!.Value)
            .IsInEnum()
            .When(command => command.RecurrenceUnit.HasValue)
            .WithMessage("A valid recurrence unit is required.");

        When(command => command.RecurrenceType == RecurrenceType.Custom, () =>
        {
            RuleFor(command => command.RecurrenceUnit)
                .NotNull()
                .WithMessage("A recurrence unit is required for a custom schedule.");
        });

        When(command => command.RecurrenceType != RecurrenceType.Custom, () =>
        {
            RuleFor(command => command.RecurrenceInterval)
                .Equal(1)
                .WithMessage(
                    "Daily, weekly, and monthly recurrence intervals must be one.");
            RuleFor(command => command)
                .Must(HasMatchingStandardUnit)
                .WithName(nameof(CreateTodoCommand.RecurrenceUnit))
                .WithMessage("The recurrence unit does not match the recurrence type.");
        });
    }

    /// <summary>
    /// A one-off may not carry schedule parts: an interval or unit without a
    /// type is more likely a mistake than an intent, so it is refused rather
    /// than ignored.
    /// </summary>
    private void ConfigureNoRecurrenceRules()
    {
        RuleFor(command => command.RecurrenceInterval)
            .Null()
            .WithMessage("A recurrence type is required when an interval is supplied.");
        RuleFor(command => command.RecurrenceUnit)
            .Null()
            .WithMessage("A recurrence type is required when a unit is supplied.");
    }
}
