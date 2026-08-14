namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// The half of the bridge a turn writes to. The loop never touches the
/// response, so it can be driven by a test that reads the same sequence the
/// browser would.
/// </summary>
public interface ITurnEventWriter
{
    ValueTask PublishAsync(TurnEvent turnEvent, CancellationToken cancellationToken);
}
