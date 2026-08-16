using FluentValidation;

using Sleeky.Todo.Application.Spaces.Validation;

namespace Sleeky.Todo.Application.Spaces.Commands.AddSpaceAccess;

public sealed class AddSpaceAccessCommandValidator : AbstractValidator<AddSpaceAccessCommand>
{
    public AddSpaceAccessCommandValidator()
    {
        RuleFor(command => command.SpaceId)
            .ValidSpaceIdentifier();

        RuleFor(command => command.SubjectId)
            .ValidSubjectIdentifier();

        RuleFor(command => command.Permission)
            .ValidSpacePermission();

        RuleFor(command => command.Version)
            .ValidExpectedVersion();
    }
}
