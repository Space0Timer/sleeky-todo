using FluentValidation;

using Sleeky.Todo.Application.Todos.Validation;

namespace Sleeky.Todo.Application.Todos.Commands.RestoreTodo;

public sealed class RestoreTodoCommandValidator : AbstractValidator<RestoreTodoCommand>
{
    public RestoreTodoCommandValidator()
    {
        RuleFor(command => command.Id)
            .ValidTodoIdentifier();

        RuleFor(command => command.Version)
            .GreaterThan(0)
            .WithMessage("Expected version must be greater than zero.");
    }
}
