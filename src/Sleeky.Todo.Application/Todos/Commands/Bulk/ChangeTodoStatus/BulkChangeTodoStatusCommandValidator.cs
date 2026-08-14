using FluentValidation;

using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.ChangeTodoStatus;

public sealed class BulkChangeTodoStatusCommandValidator
    : AbstractValidator<BulkChangeTodoStatusCommand>
{
    public BulkChangeTodoStatusCommandValidator()
    {
        RuleFor(command => command.Status)
            .IsInEnum()
            .WithMessage("A bulk status change must target a known status.");

        RuleFor(command => command.Items)
            .ValidBulkSelection();
    }
}
