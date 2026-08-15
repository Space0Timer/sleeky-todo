using System.Net;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

using Serilog;
using Serilog.Events;

using Sleeky.Todo.Api.Authentication;
using Sleeky.Todo.Api.Diagnostics;
using Sleeky.Todo.Api.ErrorHandling;
using Sleeky.Todo.Api.Hosting;
using Sleeky.Todo.Infrastructure.Persistence.Diagnostics;

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
        services.AddApiRateLimiting(configuration);
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();

        // AddControllersWithViews rather than AddControllers because
        // AutoValidateAntiforgeryTokenAttribute is a filter factory that
        // resolves its filter from the view-feature services. Under
        // AddControllers that service is missing and every action fails while
        // its filters are built, so antiforgery validation is registered
        // through the framework's own implementation instead of a hand-written
        // substitute for a security control.
        services
            .AddControllersWithViews(options =>
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
                    problem.Extensions["traceId"] = RequestTrace.Resolve(
                        context.HttpContext);

                    return new BadRequestObjectResult(problem);
                };
            });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        ForwardedHeadersSettings forwardedHeaders = configuration
            .GetSection(ForwardedHeadersSettings.SectionName)
            .Get<ForwardedHeadersSettings>()
            ?? new ForwardedHeadersSettings();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedHost
                | ForwardedHeaders.XForwardedProto;

            foreach (string proxy in forwardedHeaders.KnownProxies)
            {
                if (IPAddress.TryParse(proxy, out IPAddress? address))
                {
                    options.KnownProxies.Add(address);
                }
            }

            foreach (string network in forwardedHeaders.KnownNetworks)
            {
                if (System.Net.IPNetwork.TryParse(
                    network,
                    out System.Net.IPNetwork parsed))
                {
                    options.KnownIPNetworks.Add(parsed);
                }
            }
        });

        // Session cookies are protected with keys that live in the container
        // filesystem unless a durable location is configured. Without one a
        // restart signs every user out and two replicas cannot read each
        // other's cookies.
        string? keyRingPath = configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            services
                .AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
                .SetApplicationName("Sleeky.Todo");
        }

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        bool isDevelopment = app.Environment.IsDevelopment();

        // The forwarded host and scheme decide the OpenID Connect redirect URI
        // and the origin its correlation cookie belongs to. Development reaches
        // this host through the Vite proxy and a deployment reaches it through
        // whatever terminates TLS, so the headers are read in both; which
        // senders are believed is configuration, and defaults to loopback.
        app.UseForwardedHeaders();

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

            // This middleware wraps the pipeline, so the completion event is
            // written after the scope opened inside it has already unwound. The
            // same properties are set here rather than inherited, which is also
            // what lets the totals below be read once the request is over.
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set(
                    RequestTrace.PropertyName,
                    RequestTrace.Resolve(httpContext));

                string? userId = httpContext.User
                    .FindFirst(TodoClaimTypes.UserId)?.Value;
                if (userId is not null)
                {
                    diagnosticContext.Set(
                        RequestDiagnosticsMiddleware.UserIdPropertyName,
                        userId);
                }

                DatabaseCommandTally? tally = httpContext.Features
                    .Get<DatabaseCommandTally>();
                if (tally is not null)
                {
                    diagnosticContext.Set(
                        DatabaseCommandTally.CommandCountPropertyName,
                        tally.CommandCount);
                    diagnosticContext.Set(
                        DatabaseCommandTally.DurationPropertyName,
                        Math.Round(tally.TotalDuration.TotalMilliseconds, 1));
                }
            };
        });
        app.UseExceptionHandler();

        // Ahead of everything that writes a response, so the static files, the
        // client shell, and the API all carry the same headers. Behind the
        // exception handler, so an error response carries them too.
        //
        // The provider's origin is handed over because sign-out is a form post
        // that redirects to its end-session endpoint, and form-action is
        // checked against that redirect too.
        app.UseSecurityHeaders(
            app.Configuration.GetSection(AuthenticationSettings.SectionName)[
                nameof(AuthenticationSettings.Authority)]);

        if (!isDevelopment)
        {
            // Skipped in development because the proxied request arrives as
            // plain HTTP and would be redirected out of the client origin.
            //
            // HSTS accompanies the redirect rather than standing in for it: the
            // redirect moves the first request, and this is what stops there
            // being a first plain-HTTP request at all on every visit after it.
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        if (isDevelopment)
        {
            // Swagger middleware runs ahead of authorization, so publishing it
            // outside development would expose the whole API surface to
            // anonymous callers regardless of the fallback policy.
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // Ahead of authentication because the client shell and its bundle have
        // to reach an anonymous visitor: signing in is a route inside the
        // application, so a visitor who cannot load it can never reach the
        // point of signing in.
        app.UseStaticFiles(new StaticFileOptions
        {
            // Named rather than left to the middleware's own lookup, so the
            // files served are the ones this host reports as its web root.
            FileProvider = app.Environment.WebRootFileProvider,
            OnPrepareResponse = context =>
            {
                // Asset file names carry a content hash, so those responses
                // stay valid indefinitely. index.html must not be cached, or a
                // returning browser keeps a shell naming assets that a later
                // deployment has already removed.
                context.Context.Response.Headers.CacheControl =
                    context.Context.Request.Path.StartsWithSegments("/assets")
                        ? "public,max-age=31536000,immutable"
                        : "no-cache";
            },
        });

        // Routing is placed here rather than left to the host, which would
        // insert it ahead of every middleware above. Static files decline to
        // serve a request that already matched an endpoint, and the fallback
        // below matches every path, so leaving the order alone means no asset
        // is ever served.
        app.UseRouting();

        app.UseAuthentication();

        // After authentication, because the user is one of the two identifiers
        // it attaches, and before authorization, so a rejected request is still
        // logged as one request rather than as an anonymous fragment.
        app.UseMiddleware<RequestDiagnosticsMiddleware>();

        app.UseAuthorization();

        // After authorization, because every limit is partitioned by user and
        // an anonymous caller has already been refused by the time this runs —
        // so the limiter never has to invent a bucket for one.
        app.UseRateLimiter();

        app.MapControllers();
        app.MapHealthChecks("/health").AllowAnonymous();

        // The client routes on browser history, so a deep link such as /login
        // arrives here rather than at the client and is answered with the
        // shell. Paths that name a file, and paths under /api, are a caller's
        // mistake instead: answering those with the shell would turn a missing
        // asset into HTML parsed as JavaScript, and an unknown endpoint into a
        // parse error against a body of HTML.
        //
        // One endpoint decides all of it rather than a file fallback beside a
        // pair of catch-alls, because MapFallbackToFile matches no path that
        // names a file, and the requests it leaves unmatched reach the
        // authorization fallback policy and are answered 401.
        //
        // AllowAnonymous is required rather than defensive, for that same
        // reason: signing in is a route inside the shell, so a visitor who
        // cannot load it can never reach the point of signing in.
        // The pattern is spelled out because the default one carries a nonfile
        // constraint, which is what leaves file-like paths unmatched.
        app.MapFallback("/{**path}", async (HttpContext context) =>
        {
            PathString path = context.Request.Path;

            if (path.StartsWithSegments("/api")
                || Path.HasExtension(path.Value))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            IFileInfo shell = app.Environment.WebRootFileProvider
                .GetFileInfo("index.html");
            if (!shell.Exists)
            {
                // No client is present, which is how the API runs from source
                // while the development server serves the client itself.
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.SendFileAsync(shell);
        }).AllowAnonymous();

        return app;
    }
}
