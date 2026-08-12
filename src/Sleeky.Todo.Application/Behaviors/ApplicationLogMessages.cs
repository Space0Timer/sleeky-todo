using Microsoft.Extensions.Logging;

namespace Sleeky.Todo.Application.Behaviors;

internal static partial class ApplicationLogMessages
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Handling application request {RequestName}")]
    public static partial void HandlingRequest(ILogger logger, string requestName);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Handled application request {RequestName} in {ElapsedMilliseconds:F2} ms")]
    public static partial void HandledRequest(
        ILogger logger,
        string requestName,
        double elapsedMilliseconds);
}
