using FluentValidation;

namespace Sleeky.Todo.Application.Users.Queries.SearchUsers;

public sealed class SearchUsersQueryValidator : AbstractValidator<SearchUsersQuery>
{
    public SearchUsersQueryValidator()
    {
        RuleFor(query => query.Query)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("A search term is required.")
            .Must(term => term.Trim().Length >= UserSearchLimits.MinimumQueryLength)
            .WithMessage(
                $"A search term must be at least {UserSearchLimits.MinimumQueryLength} characters.");
    }
}
