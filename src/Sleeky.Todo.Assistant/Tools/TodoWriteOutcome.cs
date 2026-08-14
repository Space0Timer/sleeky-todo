namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// What a write hands back. <c>Changed</c> counts the TODOs whose version
/// moved, so an already-satisfied selection reports honestly instead of letting
/// the model claim it completed something that was already complete.
/// </summary>
public sealed record TodoWriteOutcome(
    int Changed,
    int Unchanged,
    IReadOnlyCollection<TodoSummary> Items);
