namespace Sleeky.Todo.Infrastructure.Persistence.Diagnostics;

/// <summary>
/// Totals the MongoDB commands one request issued, so the request's own log
/// entry can report what the database cost it.
/// </summary>
/// <remarks>
/// Ambient rather than injected, because command monitoring is configured once
/// on the singleton client and sits below every repository. Nothing on the path
/// from a handler to a command has anywhere to carry a per-request accumulator,
/// and giving it one would put a diagnostic concern into every signature it
/// crossed.
/// </remarks>
public sealed class MongoCommandTally : IDisposable
{
    public const string CommandCountPropertyName = "MongoCommands";

    public const string DurationPropertyName = "MongoMs";

    private static readonly AsyncLocal<MongoCommandTally?> CurrentTally = new AsyncLocal<MongoCommandTally?>();

    private long commandCount;
    private long durationTicks;

    public int CommandCount => (int)Interlocked.Read(ref this.commandCount);

    public TimeSpan TotalDuration => TimeSpan.FromTicks(Interlocked.Read(ref this.durationTicks));

    /// <summary>
    /// Gets the tally the calling flow belongs to, or <see langword="null"/>
    /// outside a request. Background traffic — heartbeats, connection
    /// handshakes, the index initializer — runs with no ambient tally, which is
    /// what keeps it out of a request's totals without matching command names.
    /// </summary>
    internal static MongoCommandTally? Ambient => CurrentTally.Value;

    public static MongoCommandTally BeginRequest()
    {
        MongoCommandTally tally = new MongoCommandTally();
        CurrentTally.Value = tally;

        return tally;
    }

    public void Dispose()
    {
        CurrentTally.Value = null;
    }

    /// <summary>
    /// Called from the driver's event thread, which is not the request's, so
    /// both totals are written atomically.
    /// </summary>
    internal void Add(TimeSpan duration)
    {
        Interlocked.Increment(ref this.commandCount);
        Interlocked.Add(ref this.durationTicks, duration.Ticks);
    }
}
