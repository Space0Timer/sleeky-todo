using System.Threading.Channels;

namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// Bridges the turn loop to the response. The loop runs as a task and writes
/// here; the endpoint reads the same sequence and writes it out, so neither
/// side knows about the other's transport.
/// </summary>
/// <remarks>
/// The channel is unbounded because every event is a few hundred bytes and a
/// turn produces a handful of them: a bound would let a slow reader stall the
/// loop, which holds an open transaction boundary underneath it.
/// </remarks>
public sealed class TurnEventChannel : ITurnEventWriter
{
    // One reader is guaranteed by the endpoint. One writer is not: a loop that
    // is ever allowed to invoke tools concurrently would have several, and
    // asserting otherwise here would corrupt the queue rather than fail.
    private readonly Channel<TurnEvent> channel = Channel.CreateUnbounded<TurnEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public ValueTask PublishAsync(TurnEvent turnEvent, CancellationToken cancellationToken)
    {
        return this.channel.Writer.WriteAsync(turnEvent, cancellationToken);
    }

    /// <summary>
    /// Publishes without waiting, reporting <see langword="false"/> once the
    /// turn has ended. Used by the heartbeat, which races the turn's own
    /// completion and must not turn that race into an exception.
    /// </summary>
    public bool TryPublish(TurnEvent turnEvent)
    {
        return this.channel.Writer.TryWrite(turnEvent);
    }

    /// <summary>
    /// Ends the sequence. An <paramref name="error"/> surfaces at the reader,
    /// so a turn that fails mid-stream is a faulted read rather than a stream
    /// that simply stops and looks complete.
    /// </summary>
    public void Complete(Exception? error = null)
    {
        this.channel.Writer.TryComplete(error);
    }

    public IAsyncEnumerable<TurnEvent> ReadAllAsync(CancellationToken cancellationToken)
    {
        return this.channel.Reader.ReadAllAsync(cancellationToken);
    }
}
