using FluentValidation;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk;

internal static class BulkTodoValidationRules
{
    public static IRuleBuilderOptions<T, IReadOnlyCollection<BulkTodoItemRequest>>
        ValidBulkSelection<T>(
            this IRuleBuilderInitial<T, IReadOnlyCollection<BulkTodoItemRequest>> ruleBuilder)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("At least one TODO must be selected.")
            .Must(items => items.Count <= BulkTodoLimits.MaximumSelectionSize)
            .WithMessage(
                $"No more than {BulkTodoLimits.MaximumSelectionSize} TODOs can be selected.")
            .Must(items => items.All(item => item.Id != Guid.Empty))
            .WithMessage("A TODO identifier is required.")
            .Must(items => items.Select(item => item.Id).Distinct().Count() == items.Count)
            .WithMessage("A TODO can only be selected once.")
            .Must(items => items.All(item => item.Version > 0))
            .WithMessage("Expected version must be greater than zero.");
    }
}
