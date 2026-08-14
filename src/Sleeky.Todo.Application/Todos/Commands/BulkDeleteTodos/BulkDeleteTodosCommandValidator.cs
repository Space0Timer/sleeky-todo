using FluentValidation;

using Sleeky.Todo.Application.Todos.Commands.Bulk;

namespace Sleeky.Todo.Application.Todos.Commands.BulkDeleteTodos;

public sealed class BulkDeleteTodosCommandValidator
    : AbstractValidator<BulkDeleteTodosCommand>
{
    public BulkDeleteTodosCommandValidator()
    {
        RuleFor(command => command.Items)
            .ValidBulkSelection();
    }
}
