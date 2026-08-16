using FluentValidation;

using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Validation;

/// <summary>
/// The rules more than one Space request shares, so a limit or a message is
/// stated once and every request that carries the field agrees with the
/// others.
/// </summary>
internal static class SpaceValidationRules
{
    /// <summary>
    /// The version the client last saw, which every Space mutation carries
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

    public static IRuleBuilderOptions<T, Guid> ValidSpaceIdentifier<T>(
        this IRuleBuilderInitial<T, Guid> ruleBuilder)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A Space identifier is required.");
    }

    public static IRuleBuilderOptions<T, string> ValidSpaceName<T>(
        this IRuleBuilderInitial<T, string> ruleBuilder)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A Space name is required.")
            .Must(name => name.Trim().Length <= SpaceValidationLimits.NameMaximumLength)
            .WithMessage(
                $"Space name must not exceed {SpaceValidationLimits.NameMaximumLength} characters.");
    }

    public static IRuleBuilderOptions<T, SpacePermission> ValidSpacePermission<T>(
        this IRuleBuilder<T, SpacePermission> ruleBuilder)
    {
        return ruleBuilder
            .IsInEnum()
            .WithMessage("A valid Space permission is required.");
    }

    public static IRuleBuilderOptions<T, Guid> ValidSubjectIdentifier<T>(
        this IRuleBuilderInitial<T, Guid> ruleBuilder)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A subject identifier is required.");
    }
}
