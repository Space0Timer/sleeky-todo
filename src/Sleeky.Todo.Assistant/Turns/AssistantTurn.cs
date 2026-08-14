using System.Text.Json;

namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// One request to the assistant. The transcript is held by the client and
/// echoed here, so nothing about a conversation is stored server-side.
/// </summary>
/// <remarks>
/// Tampering with the transcript gains nothing: the assistant runs with exactly
/// the caller's own rights and dispatches the same commands the caller can
/// already send over HTTP.
/// </remarks>
public sealed record AssistantTurn(
    string? Message,
    JsonElement? Transcript,
    ConfirmedAction? Confirmation);
