namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// One TODO and the version the actor last saw it at. A wire type of its own
/// rather than the command's own selection item, for the same reason the HTTP
/// contract has one: the transcript is echoed by a client and outlives any
/// single command's shape.
/// </summary>
public sealed record TodoVersionReference(Guid Id, long Version);
