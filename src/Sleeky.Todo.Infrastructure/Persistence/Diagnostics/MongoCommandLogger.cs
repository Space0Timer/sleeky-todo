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

        _ = builder.Subscribe<CommandSucceededEvent>(commandEvent =>
        {
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
        _ = builder.Subscribe<CommandFailedEvent>(commandEvent => logger.LogWarning(
            CommandFailedEventId,
            commandEvent.Failure,
            "MongoDB command {CommandName} failed after {DurationMilliseconds} ms",
            commandEvent.CommandName,
            commandEvent.Duration.TotalMilliseconds));
    }
}
