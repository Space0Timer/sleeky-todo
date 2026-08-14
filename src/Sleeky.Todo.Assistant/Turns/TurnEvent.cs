namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// One coarse step of an assistant turn. The stream carries a single sequence
/// rather than one per kind, so the payload is typed per event and the reducer
/// on the other end switches on <see cref="Type"/>.
/// </summary>
public sealed record TurnEvent
{
    private TurnEvent(string type, object? data)
    {
        Type = type;
        Data = data;
    }

    public string Type { get; }

    public object? Data { get; }

    public static TurnEvent TurnStarted()
    {
        return new TurnEvent(TurnEventType.TurnStarted, null);
    }

    public static TurnEvent ToolExecuted(ToolExecution execution)
    {
        return new TurnEvent(TurnEventType.ToolExecuted, execution);
    }

    public static TurnEvent ConfirmationRequired(ConfirmationRequest request)
    {
        return new TurnEvent(TurnEventType.ConfirmationRequired, request);
    }

    public static TurnEvent TodosChanged(TodoChangeNotice notice)
    {
        return new TurnEvent(TurnEventType.TodosChanged, notice);
    }

    public static TurnEvent Message(AssistantMessage message)
    {
        return new TurnEvent(TurnEventType.Message, message);
    }

    public static TurnEvent TurnCompleted(TurnTranscript transcript)
    {
        return new TurnEvent(TurnEventType.TurnCompleted, transcript);
    }

    public static TurnEvent Heartbeat()
    {
        return new TurnEvent(TurnEventType.Heartbeat, null);
    }
}
