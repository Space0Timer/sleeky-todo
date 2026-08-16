using FluentValidation;

using Sleeky.Todo.Application.Spaces.Validation;

namespace Sleeky.Todo.Application.Spaces.Queries.GetSpace;

public sealed class GetSpaceQueryValidator : AbstractValidator<GetSpaceQuery>
{
    public GetSpaceQueryValidator()
    {
        RuleFor(query => query.SpaceId)
            .ValidSpaceIdentifier();
    }
}
