using System.Diagnostics;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

using Serilog;
using Serilog.Events;

using Sleeky.Todo.Api.Authentication;
using Sleeky.Todo.Api.ErrorHandling;

namespace Sleeky.Todo.Api.DependencyInjection;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddApiAuthentication(configuration, environment);
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();
        services
            .AddControllers(options =>
                options.Filters.Add<AutoValidateAntiforgeryTokenAttribute>())
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
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedHost
                | ForwardedHeaders.XForwardedProto;
        });

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        bool isDevelopment = app.Environment.IsDevelopment();

        if (isDevelopment)
        {
            // The development client reaches this host through the Vite proxy,
            // so the forwarded host and scheme decide the OpenID Connect
            // redirect URI and the origin its correlation cookie belongs to.
            app.UseForwardedHeaders();
        }

        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, _, exception) =>
            {
                if (exception is not null)
                {
                    return LogEventLevel.Error;
                }

                if (httpContext.Response.StatusCode
                    >= StatusCodes.Status500InternalServerError)
                {
                    return LogEventLevel.Warning;
                }

                return httpContext.Request.Path.StartsWithSegments("/health")
                    ? LogEventLevel.Debug
                    : LogEventLevel.Information;
            };
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set(
                    "TraceId",
                    Activity.Current?.Id ?? httpContext.TraceIdentifier);

                string? userId = httpContext.User
                    .FindFirst(TodoClaimTypes.UserId)?.Value;
                if (userId is not null)
                {
                    diagnosticContext.Set("UserId", userId);
                }
            };
        });
        app.UseExceptionHandler();

        if (!isDevelopment)
        {
            // Skipped in development because the proxied request arrives as
            // plain HTTP and would be redirected out of the client origin.
            app.UseHttpsRedirection();
        }

        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health").AllowAnonymous();

        return app;
    }
}
