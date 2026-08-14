using Sleeky.Todo.Assistant.Turns;

namespace Sleeky.Todo.Assistant.Tests.Turns;

/// <summary>
/// A turn whose body is supplied by the test, so the transport can be exercised
/// without a model, a provider, or a key.
/// </summary>
internal sealed class ScriptedTurnRunner : IAssistantTurnRunner
{
    private readonly Func<ITurnEventWriter, CancellationToken, Task> script;

    public ScriptedTurnRunner(Func<ITurnEventWriter, CancellationToken, Task> script)
    {
        this.script = script;
    }

    public bool Finished { get; private set; }

    public Task RunAsync(
        AssistantTurn turn,
        ITurnEventWriter events,
        CancellationToken cancellationToken)
    {
        return RunScriptAsync(events, cancellationToken);
    }

    private async Task RunScriptAsync(
        ITurnEventWriter events,
        CancellationToken cancellationToken)
    {
        await this.script(events, cancellationToken);
        this.Finished = true;
    }
}
