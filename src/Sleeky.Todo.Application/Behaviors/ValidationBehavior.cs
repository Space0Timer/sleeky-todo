using FluentValidation;
using FluentValidation.Results;

using MediatR;

namespace Sleeky.Todo.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IReadOnlyCollection<IValidator<TRequest>> validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);

        this.validators = validators.ToArray();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (validators.Count == 0)
        {
            return await next(cancellationToken);
        }

        ValidationContext<TRequest> context = new ValidationContext<TRequest>(request);
        IEnumerable<Task<ValidationResult>> validationTasks = validators.Select(
            validator => validator.ValidateAsync(context, cancellationToken));
        ValidationResult[] validationResults = await Task.WhenAll(validationTasks);
        ValidationFailure[] failures = validationResults
            .SelectMany(result => result.Errors)
            .ToArray();

        if (failures.Length > 0)
        {
            throw new ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}
