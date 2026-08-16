using FluentValidation;

using Sleeky.Todo.Application.Todos.Validation;

namespace Sleeky.Todo.Application.Todos.Commands.AddDependency;

public sealed class AddDependencyCommandValidator : AbstractValidator<AddDependencyCommand>
{
    public AddDependencyCommandValidator()
    {
        RuleFor(command => command.Id)
            .ValidTodoIdentifier();

        RuleFor(command => command.DependencyId)
            .ValidTodoIdentifier();

        RuleFor(command => command.Version)
            .ValidExpectedVersion();
    }
}
