namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// A write committed. The client refreshes its list rather than patching it
/// from these identifiers: a bulk write can create a recurring occurrence the
/// caller never named, so the identifiers say what to look at, not what to set.
/// </summary>
public sealed record TodoChangeNotice(IReadOnlyCollection<Guid> Ids);
