using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using Sleeky.Todo.Api.Authentication;
using Sleeky.Todo.Api.Diagnostics;

namespace Sleeky.Todo.Api.Hosting;

public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Named and applied at the endpoint rather than matched by path here,
    /// because this is the limit that matters and it should be readable from
    /// the action it protects.
    /// </summary>
    public const string AssistantTurnsPolicy = "assistant-turns";

    /// <summary>
    /// The one route the policy above covers. Only it is left out of the
    /// mutation window; the assistant's settings routes are ordinary mutations
    /// and stay inside it.
    /// </summary>
    private const string AssistantTurnsPath = "/api/assistant/turns";

    /// <summary>
    /// The partition every unlimited request shares. Its value is never
    /// compared against a real key, because no limiter is created for it.
    /// </summary>
    private const string UnlimitedPartition = "unlimited";

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        RateLimitingSettings settings = configuration
            .GetSection(RateLimitingSettings.SectionName)
            .Get<RateLimitingSettings>()
            ?? new RateLimitingSettings();

        services.AddRateLimiter(options =>
        {
            options.OnRejected = WriteProblemAsync;
            options.AddPolicy(
                AssistantTurnsPolicy,
                context => PartitionAssistantTurns(context, settings));
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => PartitionMutations(context, settings));
        });

        return services;
    }

    private static RateLimitPartition<string> PartitionAssistantTurns(
        HttpContext context,
        RateLimitingSettings settings)
    {
        string? userId = ResolveUserId(context);

        if (!settings.Enabled || userId is null)
        {
            return RateLimitPartition.GetNoLimiter(UnlimitedPartition);
        }

        // Queue length zero: a caller waiting for a turn slot is holding a
        // connection open to be told to wait, which is the resource the limit
        // exists to protect. Refusing immediately says the same thing sooner.
        return RateLimitPartition.GetConcurrencyLimiter(
            userId,
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = settings.AssistantTurnConcurrency,
                QueueLimit = 0,
            });
    }

    private static RateLimitPartition<string> PartitionMutations(
        HttpContext context,
        RateLimitingSettings settings)
    {
        if (!settings.Enabled || !IsMutation(context.Request))
        {
            return RateLimitPartition.GetNoLimiter(UnlimitedPartition);
        }

        // A turn carries its own policy. Counting it here as well would make
        // the limit a user actually hits the interaction of two numbers, and
        // neither of them would describe it.
        if (context.Request.Path.Equals(AssistantTurnsPath, StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter(UnlimitedPartition);
        }

        string? userId = ResolveUserId(context);
        if (userId is null)
        {
            return RateLimitPartition.GetNoLimiter(UnlimitedPartition);
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.MutationPermitLimit,
                Window = settings.MutationWindow,
                QueueLimit = 0,
            });
    }

    /// <summary>
    /// Reads are left alone. They hold nothing past the response, and paging a
    /// long list is the one thing a well-behaved client does in a tight loop.
    /// </summary>
    private static bool IsMutation(HttpRequest request)
    {
        return !HttpMethods.IsGet(request.Method)
            && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method);
    }

    /// <summary>
    /// The claim the request logger partitions on, so a rejected request and
    /// its log entry name the same user.
    /// </summary>
    /// <remarks>
    /// Anonymous callers partition to no limiter rather than to a shared
    /// bucket: authorization has already refused them by the time this runs,
    /// and one shared partition for everything unauthenticated would let any
    /// caller exhaust it for the rest.
    /// </remarks>
    private static string? ResolveUserId(HttpContext context)
    {
        return context.User.FindFirst(TodoClaimTypes.UserId)?.Value;
    }

    private static ValueTask WriteProblemAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        HttpContext httpContext = context.HttpContext;
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // A concurrency limit has no window to wait out, so the header is
        // written only when the limiter that refused could say when.
        if (context.Lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out TimeSpan retryAfter))
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(
                retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        ProblemDetails problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests.",
            Detail = "Too many requests were made in a short time. Wait a moment "
                + "and try again.",
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["traceId"] = RequestTrace.Resolve(httpContext);

        return new ValueTask(httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken));
    }
}
