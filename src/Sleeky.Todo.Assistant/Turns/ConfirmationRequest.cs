namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// A destructive proposal, halted until a person answers it. The items are the
/// selection's state read at proposal time, and the next turn executes with
/// exactly those versions: a repeated confirmation therefore fails on the moved
/// version rather than deleting whatever has since taken their place.
/// </summary>
public sealed record ConfirmationRequest(
    string Tool,
    string Prompt,
    IReadOnlyCollection<ConfirmationItem> Items);
