namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// Lets a tool end the turn it is running inside.
/// </summary>
/// <remarks>
/// The confirmation gate needs this: a destructive proposal has to stop the
/// loop rather than hand a result back and let the model carry on. Expressed as
/// our own seam so the gate behaves identically on every provider instead of
/// depending on any framework's approval feature.
/// </remarks>
public interface ITurnController
{
    void Halt();
}
