using Microsoft.Extensions.AI;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// A model whose every reply the test writes. One entry is dequeued per round
/// of the function-calling loop, so a script reads as the sequence of calls a
/// model would make.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<ChatResponse> replies = new Queue<ChatResponse>();

    public static ChatResponse Says(string text)
    {
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
    }

    public static ChatResponse Calls(string tool, Dictionary<string, object?> arguments)
    {
        return new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            new AIContent[]
            {
                new FunctionCallContent(Guid.NewGuid().ToString(), tool, arguments),
            }));
    }

    public void Script(params ChatResponse[] responses)
    {
        this.replies.Clear();

        foreach (ChatResponse response in responses)
        {
            this.replies.Enqueue(response);
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(this.replies.Count > 0
            ? this.replies.Dequeue()
            : Says("Anything else?"));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("The turn loop does not stream from the provider.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    // The runner disposes the client it builds, and this one outlives the turn.
    public void Dispose()
    {
    }
}
