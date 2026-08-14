using System.Text.Json;

namespace Sleeky.Todo.Api.Contracts.Assistant;

/// <summary>
/// One turn. The transcript is whatever the previous turn handed back, echoed
/// unread by the client, because the server keeps no conversation history.
/// </summary>
public sealed class AssistantTurnRequest
{
    public string? Message { get; init; }

    public JsonElement? Transcript { get; init; }

    public AssistantConfirmationRequest? Confirmation { get; init; }
}
