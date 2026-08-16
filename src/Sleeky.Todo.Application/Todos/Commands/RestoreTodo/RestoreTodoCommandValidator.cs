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
            .ValidExpectedVersion();
    }
}
