using FluentValidation;

namespace Sleeky.Todo.Application.Todos.Validation;

/// <summary>
/// The rules more than one request shares, so a limit or a message is stated
/// once and every request that carries the field agrees with the others.
/// </summary>
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

    public static IRuleBuilderOptions<T, string?> MaximumSearchTextLength<T>(
        this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .Must(searchText =>
                searchText is null
                || searchText.Length <= TodoValidationLimits.SearchTextMaximumLength)
            .WithMessage(
                $"Search text must not exceed {TodoValidationLimits.SearchTextMaximumLength} characters.");
    }

    /// <summary>
    /// The version the client last saw, which every mutating command carries
    /// for optimistic concurrency. Versions start at one, so zero or less can
    /// only be an omission.
    /// </summary>
    public static IRuleBuilderOptions<T, long> ValidExpectedVersion<T>(
        this IRuleBuilder<T, long> ruleBuilder)
    {
        return ruleBuilder
            .GreaterThan(0)
            .WithMessage("Expected version must be greater than zero.");
    }

    /// <summary>
    /// The Space every TODO request acts in. Checked by validation, ahead of
    /// the access check, so a request that names no Space is a 400 rather
    /// than a lookup of the empty identifier.
    /// </summary>
    public static IRuleBuilderOptions<T, Guid> ValidSpaceIdentifier<T>(
        this IRuleBuilderInitial<T, Guid> ruleBuilder)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A Space identifier is required.");
    }

    public static IRuleBuilderOptions<T, Guid> ValidTodoIdentifier<T>(
        this IRuleBuilderInitial<T, Guid> ruleBuilder)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A TODO identifier is required.");
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
