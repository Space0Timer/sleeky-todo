namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// A tool ran to completion. The summary is what a person needs to see in the
/// transcript, not the tool's own payload: the stream is coarse by design, and
/// the model already has the full result.
/// </summary>
public sealed record ToolExecution(string Tool, string Summary, bool Succeeded);
