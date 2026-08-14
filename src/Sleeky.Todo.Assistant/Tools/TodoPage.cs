namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// One page of a list read. <c>HasMore</c> rather than the cursor itself: the
/// model needs to know its view is partial before it acts on "all of them", and
/// an opaque cursor string in the transcript is a token cost with no other use.
/// </summary>
public sealed record TodoPage(IReadOnlyCollection<TodoSummary> Items, bool HasMore);
