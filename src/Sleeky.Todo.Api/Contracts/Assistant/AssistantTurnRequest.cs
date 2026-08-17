using System.Text.Json;

namespace Sleeky.Todo.Api.Contracts.Assistant;

/// <summary>
/// One turn, in one Space. The transcript is whatever the previous turn handed
/// back, echoed unread by the client, because the server keeps no
/// conversation history.
/// </summary>
/// <remarks>
/// <see cref="SpaceId"/> is the Space the client is showing, sent on every
/// turn like the route segment on the TODO endpoints. It is required: a turn
/// with none is refused before anything runs, and a turn naming a Space the
/// caller cannot see is refused the way any request under that Space would be.
/// </remarks>
public sealed class AssistantTurnRequest
{
    public Guid SpaceId { get; init; }

    public string? Message { get; init; }

    public JsonElement? Transcript { get; init; }

    public AssistantConfirmationRequest? Confirmation { get; init; }
}
