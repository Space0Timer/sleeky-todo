using System.Diagnostics;

using Microsoft.AspNetCore.Mvc;

using Serilog;
using Serilog.Events;

using Sleeky.Todo.Api.ErrorHandling;

namespace Sleeky.Todo.Api.DependencyInjection;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();
        services
            .AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    ValidationProblemDetails problem = new ValidationProblemDetails(
                        context.ModelState)
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "Validation failed.",
                        Detail = "One or more validation errors occurred.",
                        Instance = context.HttpContext.Request.Path,
                    };
                    problem.Extensions["traceId"] = Activity.Current?.Id
                        ?? context.HttpContext.TraceIdentifier;

                    return new BadRequestObjectResult(problem);
                };
            });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, _, exception) =>
            {
                if (httpContext.Request.Path.StartsWithSegments("/health"))
                {
                    return LogEventLevel.Debug;
                }

                if (exception is not null)
                {
                    return LogEventLevel.Error;
                }

                return httpContext.Response.StatusCode
                    >= StatusCodes.Status500InternalServerError
                        ? LogEventLevel.Warning
                        : LogEventLevel.Information;
            };
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set(
                    "TraceId",
                    Activity.Current?.Id ?? httpContext.TraceIdentifier);
            };
        });
        app.UseExceptionHandler();
        app.UseHttpsRedirection();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.MapControllers();
        app.MapHealthChecks("/health");

        return app;
    }
}
