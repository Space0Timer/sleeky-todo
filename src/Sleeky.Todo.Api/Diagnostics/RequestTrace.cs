using System.Diagnostics;

namespace Sleeky.Todo.Api.Diagnostics;

/// <summary>
/// The one identifier that ties a log event, a problem response, and whatever
/// the user reads back to us to a single request.
/// </summary>
/// <remarks>
/// The trace identifier rather than <see cref="Activity.Id"/>: the latter is the
/// full W3C identifier, so it names the current span rather than the trace and
/// changes inside a child activity — which <c>HttpClient</c> starts on its own
/// for every provider call the assistant makes. Reading it from there would
/// hand out an identifier that matches nothing else in the request.
/// </remarks>
internal static class RequestTrace
{
    public const string PropertyName = "TraceId";

    public static string Resolve(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        Activity? activity = Activity.Current;

        // A hierarchical activity has no trace identifier to read: the property
        // is present but zeroed, so the legacy identifier is the only one that
        // names anything.
        if (activity is not null && activity.IdFormat == ActivityIdFormat.W3C)
        {
            return activity.TraceId.ToString();
        }

        return activity?.Id ?? httpContext.TraceIdentifier;
    }
}
