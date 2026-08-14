namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// Where a resolved connection came from. Reported to the user so they know
/// whose token budget a turn spends.
/// </summary>
public enum AssistantConnectionSource
{
    User = 0,
    Application = 1,
}
