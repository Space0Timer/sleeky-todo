using FluentValidation;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

public sealed class GetTodosQueryValidator : AbstractValidator<GetTodosQuery>
{
    public GetTodosQueryValidator()
    {
        RuleFor(query => query.Status)
            .Must(status => status is null || Enum.IsDefined(status.Value))
            .WithMessage("A valid TODO status is required.");

        RuleFor(query => query.Priority)
            .Must(priority => priority is null || Enum.IsDefined(priority.Value))
            .WithMessage("A valid TODO priority is required.");

        RuleFor(query => query.DependencyStatus)
            .Must(status => status is null || Enum.IsDefined(status.Value))
            .WithMessage("A valid dependency status is required.");

        RuleFor(query => query.Scope)
            .IsInEnum();

        RuleFor(query => query.SortField)
            .IsInEnum();

        RuleFor(query => query.SortDirection)
            .IsInEnum();

        RuleFor(query => query.Limit)
            .InclusiveBetween(1, GetTodosQuery.MaximumPageSize);

        RuleFor(query => query.DueTo)
            .GreaterThanOrEqualTo(query => query.DueFrom)
            .When(query => query.DueFrom.HasValue && query.DueTo.HasValue)
            .WithMessage("The due-to date must be on or after the due-from date.");
    }
}
