using System.Text.Json;

namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// One request to the assistant, bound to one Space. The transcript is held by
/// the client and echoed here, so nothing about a conversation is stored
/// server-side.
/// </summary>
/// <remarks>
/// The Space is trusted client context in the same sense the route segment is
/// on the HTTP path: it says where the user is working, and the runner checks
/// their access to it before anything else happens. Tampering with the
/// transcript gains nothing beyond that: the assistant runs with exactly the
/// caller's own rights in that Space and dispatches the same commands the
/// caller can already send over HTTP.
/// </remarks>
public sealed record AssistantTurn(
    Guid SpaceId,
    string? Message,
    JsonElement? Transcript,
    ConfirmedAction? Confirmation);
