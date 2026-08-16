using FluentValidation;

using Sleeky.Todo.Application.Todos.Validation;

namespace Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;

public sealed class ChangeTodoStatusCommandValidator
    : AbstractValidator<ChangeTodoStatusCommand>
{
    public ChangeTodoStatusCommandValidator()
    {
        RuleFor(command => command.Id)
            .ValidTodoIdentifier();

        RuleFor(command => command.Status)
            .IsInEnum()
            .WithMessage("A valid TODO status is required.");

        RuleFor(command => command.Version)
            .ValidExpectedVersion();
    }
}
