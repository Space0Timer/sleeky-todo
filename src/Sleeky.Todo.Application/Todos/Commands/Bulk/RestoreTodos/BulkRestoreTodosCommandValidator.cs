using FluentValidation;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.RestoreTodos;

public sealed class BulkRestoreTodosCommandValidator
    : AbstractValidator<BulkRestoreTodosCommand>
{
    public BulkRestoreTodosCommandValidator()
    {
        RuleFor(command => command.Items)
            .ValidBulkSelection();
    }
}
