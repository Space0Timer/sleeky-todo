using FluentValidation;

namespace Sleeky.Todo.Application.Tests.Behaviors;

internal sealed class TestRequestValidator : AbstractValidator<TestRequest>
{
    public TestRequestValidator()
    {
        RuleFor(request => request.Value)
            .NotEmpty();
        RuleFor(request => request.Value)
            .Must(value => value.Length >= 3)
            .WithMessage("Value must contain at least three characters.");
    }
}
