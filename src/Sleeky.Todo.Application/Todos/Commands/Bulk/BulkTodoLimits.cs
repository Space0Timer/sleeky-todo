namespace Sleeky.Todo.Application.Todos.Commands.Bulk;

public static class BulkTodoLimits
{
    /// <summary>
    /// Caps a batch so one request stays within a single MongoDB transaction's
    /// practical size and duration.
    /// </summary>
    /// <remarks>
    /// Public because callers outside this assembly declare the cap rather than
    /// discover it: the assistant states it in a tool schema so a model never
    /// composes a batch that is doomed before it is sent.
    /// </remarks>
    public const int MaximumSelectionSize = 100;
}
