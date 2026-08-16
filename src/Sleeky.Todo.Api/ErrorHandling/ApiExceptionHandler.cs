using FluentValidation;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using Sleeky.Todo.Api.Diagnostics;
using Sleeky.Todo.Application.Exceptions;

namespace Sleeky.Todo.Api.ErrorHandling;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        string traceId = RequestTrace.Resolve(httpContext);
        ProblemDetails problem = CreateProblemDetails(httpContext, exception, traceId);

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            this.logger.LogError(
                3001,
                exception,
                "Unhandled exception while processing {RequestMethod} {RequestPath}; trace ID {TraceId}",
                httpContext.Request.Method,
                httpContext.Request.Path.Value ?? string.Empty,
                traceId);
        }

        httpContext.Response.StatusCode = problem.Status
            ?? StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    private static ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        Exception exception,
        string traceId)
    {
        ProblemDetails problem = exception switch
        {
            ValidationException validationException => CreateValidationProblem(validationException),
            NotFoundException => CreateProblem(
                StatusCodes.Status404NotFound,
                "Resource not found.",
                exception.Message),
            ForbiddenException => CreateProblem(
                StatusCodes.Status403Forbidden,
                "Forbidden.",
                exception.Message),
            ConcurrencyConflictException => CreateProblem(
                StatusCodes.Status409Conflict,
                "Concurrency conflict.",
                exception.Message),
            TransactionConflictException => CreateProblem(
                StatusCodes.Status409Conflict,
                "Concurrency conflict.",
                exception.Message),
            BulkConcurrencyConflictException => CreateProblem(
                StatusCodes.Status409Conflict,
                "Concurrency conflict.",
                exception.Message),
            DomainRuleException => CreateProblem(
                StatusCodes.Status409Conflict,
                "Domain rule conflict.",
                exception.Message),
            InvalidCursorException => CreateProblem(
                StatusCodes.Status400BadRequest,
                "Invalid cursor.",
                exception.Message),
            _ => CreateProblem(
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                "An unexpected error occurred while processing the request."),
        };

        problem.Instance = httpContext.Request.Path;
        problem.Extensions["traceId"] = traceId;

        return problem;
    }

    private static ProblemDetails CreateProblem(int status, string title, string detail)
    {
        return new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        };
    }

    private static ProblemDetails CreateValidationProblem(ValidationException exception)
    {
        Dictionary<string, string[]> errors = exception.Errors
            .GroupBy(failure => ToCamelCase(failure.PropertyName))
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(failure => failure.ErrorMessage)
                    .Distinct()
                    .ToArray());
        ProblemDetails problem = CreateProblem(
            StatusCodes.Status400BadRequest,
            "Validation failed.",
            "One or more validation errors occurred.");
        problem.Extensions["errors"] = errors;

        return problem;
    }

    private static string ToCamelCase(string value)
    {
        return string.IsNullOrEmpty(value)
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];
    }
}
