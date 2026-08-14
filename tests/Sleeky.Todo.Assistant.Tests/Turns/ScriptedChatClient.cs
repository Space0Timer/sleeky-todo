using Microsoft.Extensions.AI;

namespace Sleeky.Todo.Assistant.Tests.Turns;

/// <summary>
/// A model whose every turn is written by the test.
/// </summary>
/// <remarks>
/// The loop and the gate are exercised against this rather than a provider, so
/// the suite needs no network, no key, and no non-determinism. It is also the
/// honest place to test them: the design never trusts the model with
/// correctness, so what matters is what the server does with a given sequence
/// of proposed calls — including the malformed ones a weaker model produces.
/// </remarks>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<IEnumerable<ChatMessage>, ChatResponse>> turns;

    public ScriptedChatClient(params Func<IEnumerable<ChatMessage>, ChatResponse>[] turns)
    {
        this.turns = new Queue<Func<IEnumerable<ChatMessage>, ChatResponse>>(turns);
    }

    public List<ChatOptions?> ObservedOptions { get; } = new List<ChatOptions?>();

    public static ChatResponse Says(string text)
    {
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
    }

    public static ChatResponse Calls(string tool, IDictionary<string, object?> arguments)
    {
        return new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            new AIContent[] { new FunctionCallContent(Guid.NewGuid().ToString(), tool, arguments) }));
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.ObservedOptions.Add(options);

        if (this.turns.Count == 0)
        {
            return Task.FromResult(Says("Anything else?"));
        }

        return Task.FromResult(this.turns.Dequeue()(messages));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("The turn loop does not stream from the provider.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}
