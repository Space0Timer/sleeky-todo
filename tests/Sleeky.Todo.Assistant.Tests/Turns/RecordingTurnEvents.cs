using Sleeky.Todo.Assistant.Turns;

namespace Sleeky.Todo.Assistant.Tests.Turns;

internal sealed class RecordingTurnEvents : ITurnEventWriter
{
    private readonly List<TurnEvent> published = new List<TurnEvent>();

    public IReadOnlyList<TurnEvent> Published => this.published;

    public ValueTask PublishAsync(TurnEvent turnEvent, CancellationToken cancellationToken)
    {
        this.published.Add(turnEvent);

        return ValueTask.CompletedTask;
    }

    public TData? Single<TData>(string type)
        where TData : class
    {
        return this.published
            .Where(turnEvent => turnEvent.Type == type)
            .Select(turnEvent => turnEvent.Data)
            .OfType<TData>()
            .SingleOrDefault();
    }

    public IReadOnlyList<string> Types()
    {
        return this.published.Select(turnEvent => turnEvent.Type).ToArray();
    }
}
