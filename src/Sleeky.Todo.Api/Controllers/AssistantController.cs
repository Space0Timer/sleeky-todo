using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using Sleeky.Todo.Api.Contracts.Assistant;
using Sleeky.Todo.Api.Diagnostics;
using Sleeky.Todo.Api.Hosting;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Assistant.Turns;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Api.Controllers;

/// <summary>
/// Runs an assistant turn inside the caller's own authenticated request, in
/// the Space the caller named.
/// </summary>
/// <remarks>
/// In-process and synchronous by design: the current user resolves from this
/// HTTP context, so the assistant acts as them by construction. There is no
/// impersonation and no machine credential. The one check made here is the
/// same one every Space-scoped request gets — may this user read this Space —
/// and it is made before the stream opens, because once a server-sent event
/// response has started an exception can no longer become a status code. The
/// runner repeats it as its own first step, which is what binds the scope the
/// tools then dispatch under; this copy is what turns a refusal into an
/// ordinary 404 or 403.
///
/// POST with the stream read off the response body, rather than
/// <c>EventSource</c>: antiforgery is a global requirement here and
/// <c>EventSource</c> can only issue a GET. A dropped stream loses nothing,
/// because a tool call that committed stays committed.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/assistant")]
public sealed class AssistantController : ControllerBase
{
    /// <summary>
    /// A backstop, not the bound that matters.
    /// </summary>
    /// <remarks>
    /// The turn windows the conversation before replaying it and hands the
    /// windowed copy back, so a client that echoes what it was given stays far
    /// below this. What is left to catch is a client that does not, and the
    /// host's own multi-megabyte default would let one grow long enough to be
    /// expensive first.
    /// </remarks>
    private const long MaxTurnRequestBytes = 4L * 1024 * 1024;

    private const string SpaceRequiredMessage = "A Space identifier is required.";

    private readonly IAssistantTurnRunner runner;

    private readonly ISpaceAccessService spaceAccess;

    public AssistantController(IAssistantTurnRunner runner, ISpaceAccessService spaceAccess)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(spaceAccess);

        this.runner = runner;
        this.spaceAccess = spaceAccess;
    }

    [HttpPost("turns")]
    [RequestSizeLimit(MaxTurnRequestBytes)]

    // A turn holds this request open for as long as the model takes, so the
    // bound that matters here is how many a user may have running at once
    // rather than how often they may start one.
    [EnableRateLimiting(RateLimitingServiceCollectionExtensions.AssistantTurnsPolicy)]

    // The content type is left to the result rather than declared with
    // [Produces], which installs a filter that would set it to something else.
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> Run(
        AssistantTurnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SpaceId == Guid.Empty)
        {
            return SpaceRequired();
        }

        await spaceAccess.RequireAsync(request.SpaceId, SpacePermission.Read, cancellationToken);

        // A turn is mostly waiting, so anything that holds the body back until
        // it is full defeats the point of streaming it.
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        Response.Headers["X-Accel-Buffering"] = "no";

        AssistantTurn turn = new AssistantTurn(
            request.SpaceId,
            request.Message,
            request.Transcript,
            ToConfirmation(request.Confirmation));

        return TypedResults.ServerSentEvents(ToSseItems(
            TurnEventStream.RunAsync(
                runner,
                turn,
                TurnEventStream.HeartbeatInterval,
                cancellationToken),
            cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<TurnEvent>> ToSseItems(
        IAsyncEnumerable<TurnEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (TurnEvent turnEvent in events.WithCancellation(cancellationToken))
        {
            yield return new SseItem<TurnEvent>(turnEvent, turnEvent.Type);
        }
    }

    private static ConfirmedAction? ToConfirmation(AssistantConfirmationRequest? confirmation)
    {
        if (confirmation is null)
        {
            return null;
        }

        return new ConfirmedAction(
            confirmation.Tool,
            confirmation.Items
                .Select(item => new TodoVersionReference(item.Id, item.Version))
                .ToArray());
    }

    /// <summary>
    /// The same problem shape a request that fails command validation gets:
    /// a field error under <c>errors</c>, keyed the way the JSON contract
    /// names the field. Built here because the action streams — its result
    /// is not an <c>ActionResult</c>, so the controller's own validation
    /// helpers cannot be returned from it.
    /// </summary>
    private IResult SpaceRequired()
    {
        return TypedResults.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [JsonNamingPolicy.CamelCase.ConvertName(nameof(AssistantTurnRequest.SpaceId))] =
                    [SpaceRequiredMessage],
            },
            detail: "One or more validation errors occurred.",
            instance: Request.Path,
            title: "Validation failed.",
            extensions: new Dictionary<string, object?>
            {
                [JsonNamingPolicy.CamelCase.ConvertName(RequestTrace.PropertyName)] =
                    RequestTrace.Resolve(HttpContext),
            });
    }
}
