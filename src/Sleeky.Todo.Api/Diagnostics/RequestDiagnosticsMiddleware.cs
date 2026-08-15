using Sleeky.Todo.Api.Authentication;
using Sleeky.Todo.Infrastructure.Persistence.Diagnostics;

namespace Sleeky.Todo.Api.Diagnostics;

/// <summary>
/// Gives everything logged while handling a request the identifiers needed to
/// find it again, and opens the accumulator that totals what MongoDB cost it.
/// </summary>
/// <remarks>
/// Placed after authentication, which is the first point the user is known.
/// Sign-in itself logs earlier than that and outside this scope, but it names
/// the user in its own message, so the one event this cannot cover is the one
/// event that does not need it.
/// </remarks>
public sealed class RequestDiagnosticsMiddleware
{
    public const string UserIdPropertyName = "UserId";

    private readonly RequestDelegate next;
    private readonly ILogger<RequestDiagnosticsMiddleware> logger;

    public RequestDiagnosticsMiddleware(
        RequestDelegate next,
        ILogger<RequestDiagnosticsMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);

        this.next = next;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Held on the request rather than passed along, because the code that
        // reads it — the request logger's enricher — runs outside this
        // middleware, after the ambient value has already been cleared.
        using MongoCommandTally tally = MongoCommandTally.BeginRequest();
        context.Features.Set(tally);

        using IDisposable? scope = this.logger.BeginScope(BuildScope(context));

        await this.next(context);
    }

    private static Dictionary<string, object> BuildScope(HttpContext context)
    {
        Dictionary<string, object> properties = new Dictionary<string, object>
        {
            [RequestTrace.PropertyName] = RequestTrace.Resolve(context),
        };

        string? userId = context.User.FindFirst(TodoClaimTypes.UserId)?.Value;
        if (userId is not null)
        {
            properties[UserIdPropertyName] = userId;
        }

        return properties;
    }
}
