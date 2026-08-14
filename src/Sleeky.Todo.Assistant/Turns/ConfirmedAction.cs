namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// A person's answer to a <see cref="ConfirmationRequest"/>, echoed back with
/// the versions that were displayed. The turn executes with these rather than
/// re-reading, which is what makes a replayed confirmation fail on the moved
/// version instead of acting on whatever is there now.
/// </summary>
public sealed record ConfirmedAction(
    string Tool,
    IReadOnlyCollection<TodoVersionReference> Items);
