using FluentAssertions;

using Microsoft.Extensions.AI;

using Sleeky.Todo.Assistant.Tools;
using Sleeky.Todo.Assistant.Turns;

namespace Sleeky.Todo.Assistant.Tests.Turns;

[TestClass]
public sealed class TranscriptWindowTests
{
    [TestMethod]
    public void ApplyLeavesAConversationInsideTheBoundAlone()
    {
        List<ChatMessage> messages = Conversation(5);

        TranscriptWindow.Apply(messages, 5).Should().BeFalse();

        messages.Should().HaveCount(5);
    }

    [TestMethod]
    public void ApplyDropsTheOldestExchangesAndKeepsTheOpeningMessage()
    {
        List<ChatMessage> messages = Conversation(20);

        TranscriptWindow.Apply(messages, 6).Should().BeTrue();

        messages.Should().HaveCount(6);
        messages[0].Text.Should().Be("message 0");
        messages[^1].Text.Should().Be("message 19");
    }

    /// <summary>
    /// A tool result whose call was trimmed away is an orphan the provider
    /// rejects, so the window opens after it rather than on it.
    /// </summary>
    [TestMethod]
    public void ApplyNeverOpensTheWindowOnAnOrphanedToolResult()
    {
        List<ChatMessage> messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "message 0"),
            new ChatMessage(ChatRole.User, "message 1"),
            Call("call-1"),
            Result("call-1"),
            new ChatMessage(ChatRole.Assistant, "message 4"),
        };

        // A window of three would otherwise start on the tool result.
        TranscriptWindow.Apply(messages, 3).Should().BeTrue();

        messages.Should().HaveCount(2);
        messages[0].Text.Should().Be("message 0");
        messages[1].Text.Should().Be("message 4");
        messages.Should().NotContain(message =>
            message.Contents.Any(content => content is FunctionResultContent));
    }

    /// <summary>
    /// A call and its result are one unit: keeping the call is what makes the
    /// result readable, so a window wide enough for both keeps both.
    /// </summary>
    [TestMethod]
    public void ApplyKeepsAToolResultWhoseCallSurvives()
    {
        List<ChatMessage> messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "message 0"),
            new ChatMessage(ChatRole.User, "message 1"),
            new ChatMessage(ChatRole.User, "message 2"),
            Call("call-1"),
            Result("call-1"),
        };

        TranscriptWindow.Apply(messages, 3).Should().BeTrue();

        messages.Should().HaveCount(3);
        messages[0].Text.Should().Be("message 0");
        messages[1].Contents.OfType<FunctionCallContent>().Should().ContainSingle();
        messages[2].Contents.OfType<FunctionResultContent>().Should().ContainSingle();
    }

    /// <summary>
    /// Zero or less is how a deployment asks for the unbounded replay this
    /// window replaced.
    /// </summary>
    [TestMethod]
    public void ApplyReplaysEverythingWhenTheBoundIsNotPositive()
    {
        List<ChatMessage> messages = Conversation(30);

        TranscriptWindow.Apply(messages, 0).Should().BeFalse();

        messages.Should().HaveCount(30);
    }

    [TestMethod]
    public void ApplyKeepsTheOpeningMessageEvenAtTheNarrowestBound()
    {
        List<ChatMessage> messages = Conversation(4);

        TranscriptWindow.Apply(messages, 1).Should().BeTrue();

        messages.Should().ContainSingle();
        messages[0].Text.Should().Be("message 0");
    }

    private static List<ChatMessage> Conversation(int count)
    {
        return Enumerable
            .Range(0, count)
            .Select(index => new ChatMessage(
                index % 2 == 0 ? ChatRole.User : ChatRole.Assistant,
                $"message {index}"))
            .ToList();
    }

    private static ChatMessage Call(string callId)
    {
        return new ChatMessage(
            ChatRole.Assistant,
            new AIContent[]
            {
                new FunctionCallContent(
                    callId,
                    TodoToolNames.GetTodos,
                    new Dictionary<string, object?> { ["limit"] = 5 }),
            });
    }

    private static ChatMessage Result(string callId)
    {
        return new ChatMessage(
            ChatRole.Tool,
            new AIContent[]
            {
                new FunctionResultContent(callId, "{}"),
            });
    }
}
