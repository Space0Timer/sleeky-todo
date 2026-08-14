using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

using Sleeky.Todo.Api.Contracts.Assistant;
using Sleeky.Todo.Assistant.Turns;

namespace Sleeky.Todo.Api.Controllers;

/// <summary>
/// Runs an assistant turn inside the caller's own authenticated request.
/// </summary>
/// <remarks>
/// In-process and synchronous by design: the current user resolves from this
/// HTTP context, so the assistant acts as them by construction. There is no
/// impersonation, no machine credential, and no second authorization surface to
/// keep aligned with the first.
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
    private readonly IAssistantTurnRunner runner;

    public AssistantController(IAssistantTurnRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);

        this.runner = runner;
    }

    [HttpPost("turns")]

    // The content type is left to the result rather than declared with
    // [Produces], which installs a filter that would set it to something else.
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IResult Run(AssistantTurnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A turn is mostly waiting, so anything that holds the body back until
        // it is full defeats the point of streaming it.
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        Response.Headers["X-Accel-Buffering"] = "no";

        AssistantTurn turn = new AssistantTurn(
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
}
