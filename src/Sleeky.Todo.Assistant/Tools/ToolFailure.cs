namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// A tool call that could not run as asked. It is a result rather than an
/// exception because the model is the one that has to react: a malformed call,
/// an over-cap batch, or a write against something never read are all things it
/// can fix on its next turn.
/// </summary>
public sealed record ToolFailure(string Error);
