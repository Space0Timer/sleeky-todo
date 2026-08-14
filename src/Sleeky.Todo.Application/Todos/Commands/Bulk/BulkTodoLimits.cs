namespace Sleeky.Todo.Application.Todos.Commands.Bulk;

internal static class BulkTodoLimits
{
    /// <summary>
    /// Caps a batch so one request stays within a single MongoDB transaction's
    /// practical size and duration.
    /// </summary>
    public const int MaximumSelectionSize = 100;
}
