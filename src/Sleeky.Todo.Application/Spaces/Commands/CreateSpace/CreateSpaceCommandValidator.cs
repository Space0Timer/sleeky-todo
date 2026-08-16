using FluentValidation;

using Sleeky.Todo.Application.Spaces.Validation;

namespace Sleeky.Todo.Application.Spaces.Commands.CreateSpace;

public sealed class CreateSpaceCommandValidator : AbstractValidator<CreateSpaceCommand>
{
    public CreateSpaceCommandValidator()
    {
        RuleFor(command => command.Name)
            .ValidSpaceName();
    }
}
