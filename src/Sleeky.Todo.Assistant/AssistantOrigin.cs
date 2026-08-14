using Microsoft.Extensions.Logging;

namespace Sleeky.Todo.Assistant;

/// <summary>
/// Marks the commands the assistant issues, so a log answers "did I do that or
/// did the assistant?" without the request pipeline having to know the
/// assistant exists.
/// </summary>
public static class AssistantOrigin
{
    public const string PropertyName = "RequestOrigin";

    public const string Assistant = "assistant";

    /// <summary>
    /// Opened around a dispatch. The scope is ambient, so the existing request
    /// logging behavior picks it up unchanged.
    /// </summary>
    public static IDisposable? Begin(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        return logger.BeginScope(new Dictionary<string, object>
        {
            [PropertyName] = Assistant,
        });
    }
}
