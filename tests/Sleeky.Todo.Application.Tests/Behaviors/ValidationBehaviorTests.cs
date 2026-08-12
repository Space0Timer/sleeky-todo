using FluentAssertions;

using FluentValidation;

using MediatR;

using Sleeky.Todo.Application.Behaviors;

namespace Sleeky.Todo.Application.Tests.Behaviors;

[TestClass]
public sealed class ValidationBehaviorTests
{
    [TestMethod]
    public async Task HandleAggregatesFailuresWithoutCallingNextDelegate()
    {
        TestRequestValidator validator = new TestRequestValidator();
        ValidationBehavior<TestRequest, string> behavior = new ValidationBehavior<TestRequest, string>(
            new[] { validator });
        bool nextWasCalled = false;
        RequestHandlerDelegate<string> next = _ =>
        {
            nextWasCalled = true;
            return Task.FromResult("handled");
        };

        Func<Task> act = async () =>
            await behavior.Handle(new TestRequest(string.Empty), next, CancellationToken.None);

        ValidationException exception = (await act.Should()
            .ThrowAsync<ValidationException>())
            .Which;
        exception.Errors.Should().HaveCount(2);
        nextWasCalled.Should().BeFalse();
    }

    [TestMethod]
    public async Task HandlePassesValidRequestToNextDelegate()
    {
        TestRequestValidator validator = new TestRequestValidator();
        ValidationBehavior<TestRequest, string> behavior = new ValidationBehavior<TestRequest, string>(
            new[] { validator });
        bool nextWasCalled = false;
        RequestHandlerDelegate<string> next = _ =>
        {
            nextWasCalled = true;
            return Task.FromResult("handled");
        };

        string result = await behavior.Handle(
            new TestRequest("valid"),
            next,
            CancellationToken.None);

        result.Should().Be("handled");
        nextWasCalled.Should().BeTrue();
    }

    [TestMethod]
    public async Task HandlePassesCancellationTokenToValidators()
    {
        TrackingTestRequestValidator validator = new TrackingTestRequestValidator();
        ValidationBehavior<TestRequest, string> behavior = new ValidationBehavior<TestRequest, string>(
            new[] { validator });
        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        await behavior.Handle(
            new TestRequest("valid"),
            _ => Task.FromResult("handled"),
            cancellationTokenSource.Token);

        validator.ObservedCancellationToken.Should().Be(cancellationTokenSource.Token);
    }
}

internal sealed class TestRequest
{
    public TestRequest(string value)
    {
        Value = value;
    }

    public string Value { get; }
}

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
