using Microsoft.Extensions.AI;

namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// Bounds how much of a conversation is replayed.
/// </summary>
/// <remarks>
/// The client holds the transcript and echoes it back each turn, so nothing
/// bounded it: a long conversation grew the request body, the provider's
/// context, and the tokens every later turn paid to replay it. Trimming here
/// bounds all three, because the windowed conversation is both what the model
/// is sent and what the turn hands back, so the copy the client keeps stops
/// growing as well.
///
/// What the person sees is unaffected. The client renders the chat log from
/// turn events as they arrive; the transcript is opaque to it and carried
/// rather than displayed.
///
/// The ledger is seeded from the same windowed conversation, so a TODO whose
/// read has been dropped is one the model can no longer write to. That keeps
/// "version sent equals version the actor last saw" true under trimming instead
/// of leaving a version bound to a read that is no longer in context.
/// </remarks>
public static class TranscriptWindow
{
    /// <summary>
    /// Drops the oldest messages until at most <paramref name="maxMessages"/>
    /// remain.
    /// </summary>
    /// <remarks>
    /// The opening message always survives. It carries the date and the
    /// person's name, and it is the still prefix that prompt caching depends
    /// on, so dropping it would both lose the conversation's context and move
    /// the cacheable prefix on every turn.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> when messages were removed, which is what tells
    /// the caller to seed the ledger from what is left rather than from the
    /// transcript that arrived.
    /// </returns>
    public static bool Apply(List<ChatMessage> messages, int maxMessages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (maxMessages <= 0 || messages.Count <= maxMessages)
        {
            return false;
        }

        // The opening message is kept outside the window, so the window itself
        // holds one fewer.
        int start = messages.Count - (maxMessages - 1);

        // A tool result whose call has been trimmed away is an orphan, which
        // providers reject outright. Advancing past one drops slightly more
        // than asked rather than sending a conversation that cannot be read.
        while (start < messages.Count && CarriesFunctionResult(messages[start]))
        {
            start++;
        }

        if (start <= 1)
        {
            return false;
        }

        messages.RemoveRange(1, start - 1);
        return true;
    }

    private static bool CarriesFunctionResult(ChatMessage message)
    {
        return message.Contents.Any(content => content is FunctionResultContent);
    }
}
