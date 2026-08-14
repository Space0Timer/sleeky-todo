using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.AI;

using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Assistant.Tools;
using Sleeky.Todo.Assistant.Turns;

namespace Sleeky.Todo.Assistant.Tests.Turns;

[TestClass]
public sealed class TranscriptCodecTests
{
    private static readonly Guid First = TestTodo.Id("todo-1");

    /// <summary>
    /// The client echoes back exactly what the last turn handed it, so what is
    /// written has to be readable by the next turn — including the tool calls
    /// and results, which are what the ledger is later recovered from.
    /// </summary>
    [TestMethod]
    public void WriteAndReadRoundTripAConversationIncludingToolTraffic()
    {
        List<ChatMessage> original = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "What's due today?"),
            new ChatMessage(
                ChatRole.Assistant,
                new AIContent[]
                {
                    new FunctionCallContent(
                        "call-1",
                        TodoToolNames.GetTodos,
                        new Dictionary<string, object?> { ["limit"] = 5 }),
                }),
            new ChatMessage(
                ChatRole.Tool,
                new AIContent[]
                {
                    new FunctionResultContent(
                        "call-1",
                        new TodoPage(
                            new[]
                            {
                                new TodoSummary(
                                    First,
                                    "Submit report",
                                    11,
                                    TestTodo.DueDate,
                                    "NotStarted",
                                    "High",
                                    IsDeleted: false,
                                    IsBlocked: false),
                            },
                            HasMore: false)),
                }),
            new ChatMessage(ChatRole.Assistant, "One is due today."),
        };

        List<ChatMessage> restored = TranscriptCodec.Read(TranscriptCodec.Write(original));

        restored.Should().HaveCount(4);
        restored.Select(message => message.Role).Should().Equal(
            ChatRole.User,
            ChatRole.Assistant,
            ChatRole.Tool,
            ChatRole.Assistant);
        restored[3].Text.Should().Be("One is due today.");
    }

    [TestMethod]
    public void SeedLedgerRecoversVersionsFromASerializedToolResult()
    {
        JsonElement transcript = TranscriptCodec.Write(new List<ChatMessage>
        {
            new ChatMessage(
                ChatRole.Tool,
                new AIContent[]
                {
                    new FunctionResultContent(
                        "call-1",
                        new TodoPage(
                            new[]
                            {
                                new TodoSummary(
                                    First,
                                    "Submit report",
                                    11,
                                    TestTodo.DueDate,
                                    "NotStarted",
                                    "High",
                                    IsDeleted: false,
                                    IsBlocked: false),
                            },
                            HasMore: false)),
                }),
        });
        TodoVersionLedger ledger = new TodoVersionLedger();

        TranscriptCodec.SeedLedger(transcript, ledger);

        ledger.TryBind(
            new[] { First },
            out IReadOnlyCollection<BulkTodoItemRequest> bound,
            out _)
            .Should().BeTrue();
        bound.Single().Version.Should().Be(11);
    }

    /// <summary>
    /// Some providers hand a tool result back as a JSON string rather than as
    /// structured content, which would otherwise hide every version behind one
    /// opaque value.
    /// </summary>
    [TestMethod]
    public void SeedLedgerLooksInsideAToolResultCarriedAsText()
    {
        using JsonDocument transcript = JsonDocument.Parse(
            $$"""
            [ { "role": "tool", "text": "{\"items\":[{\"id\":\"{{First}}\",\"version\":9}]}" } ]
            """);
        TodoVersionLedger ledger = new TodoVersionLedger();

        TranscriptCodec.SeedLedger(transcript.RootElement, ledger);

        ledger.TryBind(
            new[] { First },
            out IReadOnlyCollection<BulkTodoItemRequest> bound,
            out _)
            .Should().BeTrue();
        bound.Single().Version.Should().Be(9);
    }

    /// <summary>
    /// There is nothing to protect here — the assistant runs with the caller's
    /// own rights — so a mangled transcript starts a fresh conversation rather
    /// than failing the turn.
    /// </summary>
    [TestMethod]
    public void ReadStartsFreshRatherThanFailingOnUnusableContent()
    {
        using JsonDocument notATranscript = JsonDocument.Parse("""{"nope":true}""");

        TranscriptCodec.Read(notATranscript.RootElement).Should().BeEmpty();
        TranscriptCodec.Read(null).Should().BeEmpty();
    }

    [TestMethod]
    public void EmptyIsAnArrayTheClientCanEchoBack()
    {
        JsonElement empty = TranscriptCodec.Empty();

        empty.ValueKind.Should().Be(JsonValueKind.Array);
        TranscriptCodec.Read(empty).Should().BeEmpty();
    }
}
