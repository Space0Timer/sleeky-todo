using FluentValidation;

using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.BulkChangeTodoStatus;

public sealed class BulkChangeTodoStatusCommandValidator
    : AbstractValidator<BulkChangeTodoStatusCommand>
{
    public BulkChangeTodoStatusCommandValidator()
    {
        // Reopening and unarchiving stay single-item until a client needs them
        // in bulk; widening this rule is all that would be required.
        RuleFor(command => command.Status)
            .Must(status => status is TodoStatus.Completed or TodoStatus.Archived)
            .WithMessage("A bulk status change must target Completed or Archived.");

        RuleFor(command => command.Items)
            .ValidBulkSelection();
    }
}
