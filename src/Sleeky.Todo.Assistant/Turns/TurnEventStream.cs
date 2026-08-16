using System.Runtime.CompilerServices;

namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// Turns a running turn into the sequence of events an endpoint streams out.
/// The turn runs as a task and writes to a channel; this reads the channel,
/// keeps the stream alive while the model thinks, and makes sure the turn has
/// finished before the response does.
/// </summary>
public static class TurnEventStream
{
    /// <summary>
    /// Short enough to stay under the idle timeout of a proxy that has not been
    /// configured for streaming, which is the failure this exists to prevent.
    /// </summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Runs a turn and yields what it reports, beating in the gaps.
    /// </summary>
    /// <remarks>
    /// Heartbeats are published into the same channel rather than interleaved
    /// at the reader. Interleaving would mean racing the reader's own
    /// <c>MoveNextAsync</c> against a timer, and abandoning that read — which is
    /// what a disconnected browser does — throws from the channel enumerator's
    /// disposal. Here the only read is a plain <c>await foreach</c>, so leaving
    /// early is suspended at a yield and disposes cleanly.
    /// </remarks>
    public static async IAsyncEnumerable<TurnEvent> RunAsync(
        IAssistantTurnRunner runner,
        AssistantTurn turn,
        TimeSpan heartbeatInterval,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(turn);

        TurnEventChannel channel = new TurnEventChannel();
        using CancellationTokenSource beating =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task running = RunTurnAsync(runner, turn, channel, cancellationToken);
        Task beat = BeatAsync(channel, heartbeatInterval, beating.Token);

        try
        {
            await foreach (TurnEvent turnEvent in channel.ReadAllAsync(cancellationToken))
            {
                yield return turnEvent;
            }
        }
        finally
        {
            // Awaited rather than abandoned: the turn dispatches through scoped
            // services owned by the request, so letting it outlive the response
            // would use a disposed container. Neither task throws, and the
            // channel is unbounded, so a reader that left early cannot wedge
            // the turn behind a write it will never accept.
            await beating.CancelAsync();
            await beat;
            await running;
        }
    }

    private static async Task RunTurnAsync(
        IAssistantTurnRunner runner,
        AssistantTurn turn,
        TurnEventChannel channel,
        CancellationToken cancellationToken)
    {
        // The turn runs detached from its reader, so a failure has nowhere else
        // to go and is handed to the reader instead. Catching something
        // narrower would leave an unexpected failure as a stream that simply
        // stops, which is indistinguishable from a turn that finished.
        try
        {
            await runner.RunAsync(turn, channel, cancellationToken);
            channel.Complete();
        }
        catch (Exception exception)
        {
            channel.Complete(exception);
        }
    }

    private static async Task BeatAsync(
        TurnEventChannel channel,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                // A closed channel means the turn ended between the tick and
                // the write, which is a beat with nothing left to keep alive.
                if (!channel.TryPublish(TurnEvent.Heartbeat()))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The turn finished or the caller left.
        }
    }
}
