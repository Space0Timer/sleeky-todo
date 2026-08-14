using Microsoft.Extensions.AI;

using Sleeky.Todo.Assistant.Tools;

namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// Ends the turn from inside a tool call.
/// </summary>
/// <remarks>
/// This is loop control, not an approval feature: the gate itself is ours, so
/// it behaves identically on every provider rather than depending on what any
/// one of them offers.
/// </remarks>
public sealed class FunctionInvocationTurnController : ITurnController
{
    public void Halt()
    {
        FunctionInvocationContext? context = FunctionInvokingChatClient.CurrentContext;

        // Null when a tool is called outside a loop, which is how the tools are
        // exercised directly by a test. Nothing to stop in that case.
        if (context is not null)
        {
            context.Terminate = true;
        }
    }
}
