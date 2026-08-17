using FluentValidation;

using Sleeky.Todo.Application.Spaces.Validation;

namespace Sleeky.Todo.Application.Spaces.Commands.RenameSpace;

public sealed class RenameSpaceCommandValidator : AbstractValidator<RenameSpaceCommand>
{
    public RenameSpaceCommandValidator()
    {
        RuleFor(command => command.SpaceId)
            .ValidSpaceIdentifier();

        RuleFor(command => command.Name)
            .ValidSpaceName();

        RuleFor(command => command.Version)
            .ValidExpectedVersion();
    }
}
