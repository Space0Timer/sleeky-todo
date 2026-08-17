using FluentValidation;

using Sleeky.Todo.Application.Todos.Validation;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.DeleteTodos;

public sealed class BulkDeleteTodosCommandValidator
    : AbstractValidator<BulkDeleteTodosCommand>
{
    public BulkDeleteTodosCommandValidator()
    {
        RuleFor(command => command.SpaceId)
            .ValidSpaceIdentifier();

        RuleFor(command => command.Items)
            .ValidBulkSelection();
    }
}
