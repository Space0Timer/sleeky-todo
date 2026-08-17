using FluentValidation;

using Sleeky.Todo.Application.Todos.Validation;

namespace Sleeky.Todo.Application.Todos.Commands.DeleteTodo;

public sealed class DeleteTodoCommandValidator : AbstractValidator<DeleteTodoCommand>
{
    public DeleteTodoCommandValidator()
    {
        RuleFor(command => command.SpaceId)
            .ValidSpaceIdentifier();

        RuleFor(command => command.Id)
            .ValidTodoIdentifier();

        RuleFor(command => command.Version)
            .ValidExpectedVersion();
    }
}
