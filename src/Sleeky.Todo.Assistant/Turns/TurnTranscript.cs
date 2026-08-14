using System.Text.Json;

namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// The conversation as it stands after a turn, for the client to hold and echo
/// into the next one. It closes the turn because the server keeps no history:
/// without it the next turn would start from nothing.
/// </summary>
public sealed record TurnTranscript(JsonElement Messages);
