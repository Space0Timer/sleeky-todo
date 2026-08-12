using FluentValidation;

namespace Sleeky.Todo.Application.Tests.Behaviors;

internal sealed class TrackingTestRequestValidator : AbstractValidator<TestRequest>
{
    public TrackingTestRequestValidator()
    {
        RuleFor(request => request.Value)
            .MustAsync((_, cancellationToken) =>
            {
                ObservedCancellationToken = cancellationToken;
                return Task.FromResult(true);
            });
    }

    public CancellationToken ObservedCancellationToken { get; private set; }
}
