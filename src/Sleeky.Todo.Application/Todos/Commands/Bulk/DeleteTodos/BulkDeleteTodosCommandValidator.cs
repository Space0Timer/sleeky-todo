using FluentValidation;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.DeleteTodos;

public sealed class BulkDeleteTodosCommandValidator
    : AbstractValidator<BulkDeleteTodosCommand>
{
    public BulkDeleteTodosCommandValidator()
    {
        RuleFor(command => command.Items)
            .ValidBulkSelection();
    }
}
