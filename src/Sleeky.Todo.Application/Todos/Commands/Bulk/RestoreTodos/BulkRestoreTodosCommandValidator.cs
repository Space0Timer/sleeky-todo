using FluentValidation;

using Sleeky.Todo.Application.Todos.Validation;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.RestoreTodos;

public sealed class BulkRestoreTodosCommandValidator
    : AbstractValidator<BulkRestoreTodosCommand>
{
    public BulkRestoreTodosCommandValidator()
    {
        RuleFor(command => command.SpaceId)
            .ValidSpaceIdentifier();

        RuleFor(command => command.Items)
            .ValidBulkSelection();
    }
}
