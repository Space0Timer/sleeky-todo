namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// Runs one turn, reporting progress through <paramref name="events"/> rather
/// than returning it, because a turn is watched while it happens.
/// </summary>
public interface IAssistantTurnRunner
{
    Task RunAsync(
        AssistantTurn turn,
        ITurnEventWriter events,
        CancellationToken cancellationToken);
}
