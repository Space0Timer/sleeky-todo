using FluentValidation;

using Sleeky.Todo.Application.Todos.Commands.Bulk;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;

public sealed class GetTodoSelectionQueryValidator : AbstractValidator<GetTodoSelectionQuery>
{
    public GetTodoSelectionQueryValidator()
    {
        RuleFor(query => query.Ids)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("At least one TODO must be selected.")
            .Must(ids => ids.Count <= BulkTodoLimits.MaximumSelectionSize)
            .WithMessage(
                $"No more than {BulkTodoLimits.MaximumSelectionSize} TODOs can be selected.")
            .Must(ids => ids.All(id => id != Guid.Empty))
            .WithMessage("A TODO identifier is required.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("A TODO can only be selected once.");
    }
}
