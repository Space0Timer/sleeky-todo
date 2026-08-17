using FluentValidation;

using Sleeky.Todo.Application.Todos.Validation;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodo;

public sealed class GetTodoQueryValidator : AbstractValidator<GetTodoQuery>
{
    public GetTodoQueryValidator()
    {
        RuleFor(query => query.SpaceId)
            .ValidSpaceIdentifier();

        RuleFor(query => query.Id)
            .ValidTodoIdentifier();
    }
}
