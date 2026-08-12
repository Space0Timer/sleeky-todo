using FluentValidation;

using Sleeky.Todo.Application.Todos.Validation;

namespace Sleeky.Todo.Application.Todos.Commands.RemoveDependency;

public sealed class RemoveDependencyCommandValidator : AbstractValidator<RemoveDependencyCommand>
{
    public RemoveDependencyCommandValidator()
    {
        RuleFor(command => command.Id)
            .ValidTodoIdentifier();

        RuleFor(command => command.DependencyId)
            .ValidTodoIdentifier();

        RuleFor(command => command.Version)
            .GreaterThan(0)
            .WithMessage("Expected version must be greater than zero.");
    }
}
