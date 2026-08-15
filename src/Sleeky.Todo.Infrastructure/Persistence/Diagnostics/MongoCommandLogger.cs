using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using MongoDB.Driver.Core.Configuration;
using MongoDB.Driver.Core.Events;

namespace Sleeky.Todo.Infrastructure.Persistence.Diagnostics;

/// <summary>
/// Records how long each MongoDB command took. Command monitoring sits below the
/// repositories, so it also observes reads that never reach a transaction, which
/// is why timings are collected here rather than around the write abstractions.
/// </summary>
internal static class MongoCommandLogger
{
    public const string LoggerCategory = "Sleeky.Todo.Infrastructure.Persistence.MongoCommands";

    private const int CommandSucceededEventId = 2101;
    private const int CommandFailedEventId = 2102;

    public static void Configure(ClusterBuilder builder, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(logger);

        // A command's start and its outcome are separate events, raised on
        // threads that need not be the caller's, so the tally to credit is
        // captured while the caller's flow is still current and looked up again
        // by the identifier the driver puts on both. Scoped to this client
        // rather than held statically, because a process can run more than one —
        // every integration test host builds its own.
        ConcurrentDictionary<int, DatabaseCommandTally> inFlight =
            new ConcurrentDictionary<int, DatabaseCommandTally>();

        _ = builder.Subscribe<CommandStartedEvent>(commandEvent =>
        {
            DatabaseCommandTally? tally = DatabaseCommandTally.Ambient;
            if (tally is not null)
            {
                inFlight[commandEvent.RequestId] = tally;
            }
        });

        _ = builder.Subscribe<CommandSucceededEvent>(commandEvent =>
        {
            Record(inFlight, commandEvent.RequestId, commandEvent.Duration);

            // Checked around the log call alone. The totals above are what the
            // request's own entry reports, so they are collected whatever level
            // this category is running at.
            if (!logger.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            logger.LogDebug(
                CommandSucceededEventId,
                "MongoDB command {CommandName} succeeded in {DurationMilliseconds} ms",
                commandEvent.CommandName,
                commandEvent.Duration.TotalMilliseconds);
        });

        _ = builder.Subscribe<CommandFailedEvent>(commandEvent =>
        {
            Record(inFlight, commandEvent.RequestId, commandEvent.Duration);

            logger.LogWarning(
                CommandFailedEventId,
                commandEvent.Failure,
                "MongoDB command {CommandName} failed after {DurationMilliseconds} ms",
                commandEvent.CommandName,
                commandEvent.Duration.TotalMilliseconds);
        });
    }

    private static void Record(
        ConcurrentDictionary<int, DatabaseCommandTally> inFlight,
        int requestId,
        TimeSpan duration)
    {
        if (inFlight.TryRemove(requestId, out DatabaseCommandTally? tally))
        {
            tally.Add(duration);
        }
    }
}
