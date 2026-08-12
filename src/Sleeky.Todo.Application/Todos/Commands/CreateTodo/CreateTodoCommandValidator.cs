using FluentValidation;

using Sleeky.Todo.Application.Todos.Validation;

namespace Sleeky.Todo.Application.Todos.Commands.CreateTodo;

public sealed class CreateTodoCommandValidator : AbstractValidator<CreateTodoCommand>
{
    public CreateTodoCommandValidator()
    {
        RuleFor(command => command.Name)
            .ValidTodoName();

        RuleFor(command => command.Description)
            .MaximumTrimmedDescriptionLength();

        RuleFor(command => command.DueDate)
            .NotEqual(default(DateOnly))
            .WithMessage("A valid TODO due date is required.");

        RuleFor(command => command.Priority)
            .IsInEnum()
            .WithMessage("A valid TODO priority is required.");
    }
}
