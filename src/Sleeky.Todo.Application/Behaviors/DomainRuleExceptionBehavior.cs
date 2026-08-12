using MediatR;

using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Behaviors;

public sealed class DomainRuleExceptionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (DomainException exception)
        {
            throw new DomainRuleException(exception.Message, exception);
        }
    }
}
