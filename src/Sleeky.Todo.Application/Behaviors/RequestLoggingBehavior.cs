using System.Diagnostics;

using MediatR;

using Microsoft.Extensions.Logging;

namespace Sleeky.Todo.Application.Behaviors;

public sealed class RequestLoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<RequestLoggingBehavior<TRequest, TResponse>> logger;

    public RequestLoggingBehavior(
        ILogger<RequestLoggingBehavior<TRequest, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        string requestName = typeof(TRequest).Name;
        this.logger.LogDebug(
            1001,
            "Handling application request {RequestName}",
            requestName);
        long startedAt = Stopwatch.GetTimestamp();

        TResponse response = await next(cancellationToken);

        this.logger.LogInformation(
            1002,
            "Handled application request {RequestName} in {ElapsedMilliseconds:F2} ms",
            requestName,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        return response;
    }
}
