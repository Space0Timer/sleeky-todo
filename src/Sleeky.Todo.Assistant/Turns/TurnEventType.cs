namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// Wire names for the events a turn emits. They are part of the client
/// contract, so they are named once here rather than spelled at each emit site
/// and again in the reducer that reads them.
/// </summary>
public static class TurnEventType
{
    public const string TurnStarted = "turn_started";

    public const string ToolExecuted = "tool_executed";

    public const string ConfirmationRequired = "confirmation_required";

    public const string TodosChanged = "todos_changed";

    public const string Message = "message";

    /// <summary>
    /// Closes the turn and carries the conversation forward. The server keeps
    /// no history, so a turn that ended without handing the transcript back
    /// would leave the next one with nothing to continue from.
    /// </summary>
    public const string TurnCompleted = "turn_completed";

    /// <summary>
    /// Transport rather than turn: it keeps an idle stream, and any proxy
    /// between it and the browser, from timing out while the model thinks.
    /// Clients ignore it.
    /// </summary>
    public const string Heartbeat = "heartbeat";
}
