using FluentValidation;

namespace Sleeky.Todo.Application.Todos.Validation;

internal static class TodoValidationRules
{
    public static IRuleBuilderOptions<T, string?> MaximumTrimmedDescriptionLength<T>(
        this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .Must(description =>
                description is null
                || description.Trim().Length <= TodoValidationLimits.DescriptionMaximumLength)
            .WithMessage(
                $"Description must not exceed {TodoValidationLimits.DescriptionMaximumLength} characters.");
    }

    public static IRuleBuilderOptions<T, string> ValidTodoIdentifier<T>(
        this IRuleBuilderInitial<T, string> ruleBuilder)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A TODO identifier is required.")
            .MaximumLength(TodoValidationLimits.IdentifierMaximumLength)
            .WithMessage(
                $"TODO identifier must not exceed {TodoValidationLimits.IdentifierMaximumLength} characters.")
            .Must(identifier => identifier == identifier.Trim())
            .WithMessage("TODO identifier must not contain leading or trailing whitespace.");
    }

    public static IRuleBuilderOptions<T, string> ValidTodoName<T>(
        this IRuleBuilderInitial<T, string> ruleBuilder)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A TODO name is required.")
            .Must(name => name.Trim().Length <= TodoValidationLimits.NameMaximumLength)
            .WithMessage(
                $"TODO name must not exceed {TodoValidationLimits.NameMaximumLength} characters.");
    }
}
