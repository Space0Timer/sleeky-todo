using FluentValidation;

using Sleeky.Todo.Application.Spaces.Validation;

namespace Sleeky.Todo.Application.Spaces.Commands.RemoveSpaceAccess;

public sealed class RemoveSpaceAccessCommandValidator
    : AbstractValidator<RemoveSpaceAccessCommand>
{
    public RemoveSpaceAccessCommandValidator()
    {
        RuleFor(command => command.SpaceId)
            .ValidSpaceIdentifier();

        RuleFor(command => command.SubjectId)
            .ValidSubjectIdentifier();

        RuleFor(command => command.Version)
            .ValidExpectedVersion();
    }
}
