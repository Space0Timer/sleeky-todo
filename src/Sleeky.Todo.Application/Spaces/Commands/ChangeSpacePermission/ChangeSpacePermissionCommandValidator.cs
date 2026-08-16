using FluentValidation;

using Sleeky.Todo.Application.Spaces.Validation;

namespace Sleeky.Todo.Application.Spaces.Commands.ChangeSpacePermission;

public sealed class ChangeSpacePermissionCommandValidator
    : AbstractValidator<ChangeSpacePermissionCommand>
{
    public ChangeSpacePermissionCommandValidator()
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
