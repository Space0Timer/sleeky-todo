using System.Text.Json;

using FluentAssertions;

using MediatR;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;
using Sleeky.Todo.Assistant.Conflicts;
using Sleeky.Todo.Assistant.Providers;
using Sleeky.Todo.Assistant.Tools;
using Sleeky.Todo.Assistant.Turns;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Assistant.Tests.Turns;

[TestClass]
public sealed class AssistantTurnRunnerTests
{
    private static readonly Guid First = TestTodo.Id("todo-1");

    [TestMethod]
    public async Task RunReportsWhenNoProviderIsConfigured()
    {
        Harness harness = new Harness(connection: null);

        await harness.RunAsync(new AssistantTurn(TestTodo.SpaceId, "Complete everything.", null, null));

        harness.Events.Types().Should().Equal(
            TurnEventType.TurnStarted,
            TurnEventType.Message,
            TurnEventType.TurnCompleted);
        harness.Events.Single<AssistantMessage>(TurnEventType.Message)!
            .Text.Should().Contain("assistant settings");
    }

    [TestMethod]
    public async Task RunStreamsTheAnswerAndHandsTheConversationBack()
    {
        Harness harness = new Harness(
            ScriptedChatClient.Says("You have three TODOs due this week."));

        await harness.RunAsync(new AssistantTurn(TestTodo.SpaceId, "What's due?", null, null));

        harness.Events.Types().Should().Equal(
            TurnEventType.TurnStarted,
            TurnEventType.Message,
            TurnEventType.TurnCompleted);

        TurnTranscript? transcript =
            harness.Events.Single<TurnTranscript>(TurnEventType.TurnCompleted);
        transcript.Should().NotBeNull();
        transcript!.Messages.ValueKind.Should().Be(JsonValueKind.Array);
        transcript.Messages.GetArrayLength().Should().BeGreaterThan(1);
    }

    /// <summary>
    /// A proposal ends the turn. The loop must not carry on and let the model
    /// narrate a deletion nobody has agreed to.
    /// </summary>
    [TestMethod]
    public async Task RunHaltsOnADeletionProposalWithoutDeletingAnything()
    {
        Harness harness = new Harness(
            ScriptedChatClient.Calls(
                TodoToolNames.DeleteTodos,
                new Dictionary<string, object?>
                {
                    ["ids"] = new[] { First.ToString() },
                }));
        harness.StageSelection(TestTodo.At(First, 5, "Old draft"));

        await harness.RunAsync(new AssistantTurn(TestTodo.SpaceId, "Delete the old draft.", null, null));

        harness.Events.Types().Should().Contain(TurnEventType.ConfirmationRequired);
        await harness.Policy.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default);

        ConfirmationRequest request =
            harness.Events.Single<ConfirmationRequest>(TurnEventType.ConfirmationRequired)!;
        request.Items.Single().Version.Should().Be(5);
        request.Items.Single().Name.Should().Be("Old draft");
    }

    /// <summary>
    /// The confirming turn executes what the person agreed to rather than
    /// asking the model to propose it again.
    /// </summary>
    [TestMethod]
    public async Task RunAppliesAConfirmedDeletionWithTheDisplayedVersions()
    {
        Harness harness = new Harness(ScriptedChatClient.Says("Deleted one TODO."));
        harness.Policy
            .DeleteAsync(TestTodo.SpaceId, Arg.Any<IReadOnlyCollection<BulkTodoItemRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new BulkTodoResult(new[]
            {
                new BulkTodoResultItem(First, 6, TodoStatus.Open, TestTodo.Timestamp, null),
            }));

        await harness.RunAsync(new AssistantTurn(
            TestTodo.SpaceId,
            null,
            null,
            new ConfirmedAction(
                TodoToolNames.DeleteTodos,
                new[] { new TodoVersionReference(First, 5) })));

        await harness.Policy.Received(1).DeleteAsync(
            TestTodo.SpaceId,
            Arg.Is<IReadOnlyCollection<BulkTodoItemRequest>>(items =>
                items.Single().Id == First && items.Single().Version == 5),
            Arg.Any<CancellationToken>());
        harness.Events.Types().Should().Contain(TurnEventType.TodosChanged);
    }

    /// <summary>
    /// Deletion is the only tool that proposes, so a confirmation naming
    /// anything else is a client that has reused this path. Running a deletion
    /// for it would invert the intent the gate exists to check.
    /// </summary>
    [TestMethod]
    public async Task RunRefusesAConfirmationThatNamesAnotherTool()
    {
        Harness harness = new Harness(ScriptedChatClient.Says("Nothing to summarise."));

        await harness.RunAsync(new AssistantTurn(
            TestTodo.SpaceId,
            null,
            null,
            new ConfirmedAction(
                TodoToolNames.RestoreTodos,
                new[] { new TodoVersionReference(First, 5) })));

        await harness.Policy.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default);
        await harness.Policy.DidNotReceiveWithAnyArgs().RestoreAsync(default, default!, default);
        harness.Events.Types().Should().NotContain(TurnEventType.TodosChanged);
    }

    /// <summary>
    /// Losing a provider mid-session is recoverable, so the conversation is
    /// handed back rather than cleared.
    /// </summary>
    [TestMethod]
    public async Task RunKeepsTheConversationWhenNoProviderIsConfigured()
    {
        using JsonDocument earlier = JsonDocument.Parse("""[{"role":"user","text":"hello"}]""");
        Harness harness = new Harness(connection: null);

        await harness.RunAsync(new AssistantTurn(TestTodo.SpaceId, null, earlier.RootElement, null));

        TurnTranscript? handedBack =
            harness.Events.Single<TurnTranscript>(TurnEventType.TurnCompleted);
        handedBack.Should().NotBeNull();
        handedBack!.Messages.GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// The tool set never varies, and the dynamic context is a user message, so
    /// the cacheable prefix stays still across turns.
    /// </summary>
    [TestMethod]
    public async Task RunSendsAStableToolSetAndKeepsTheDateOutOfTheSystemPrompt()
    {
        Harness harness = new Harness(ScriptedChatClient.Says("Sure."));

        await harness.RunAsync(new AssistantTurn(TestTodo.SpaceId, "Hello.", null, null));

        ChatOptions options = harness.Client!.ObservedOptions.Single()!;
        options.Tools.Should().HaveCount(6);
        options.Instructions.Should().Be(AssistantSystemPrompt.Text);
        options.Instructions.Should().NotContain("2026-08-14");
    }

    /// <summary>
    /// The opening context names the Space, because the tools act only inside
    /// it and the model should say so when asked. Exact text: it is written
    /// once and carried in the transcript for the life of the conversation.
    /// </summary>
    [TestMethod]
    public async Task RunOpensTheConversationWithTodaysDateAndTheSpaceAsAUserMessage()
    {
        List<ChatMessage> seen = new List<ChatMessage>();
        Harness harness = new Harness(messages =>
        {
            seen.AddRange(messages);
            return ScriptedChatClient.Says("Sure.");
        });

        await harness.RunAsync(new AssistantTurn(TestTodo.SpaceId, "Hello.", null, null));

        seen.Should().HaveCount(2);
        seen[0].Role.Should().Be(ChatRole.User);
        seen[0].Text.Should().Be(
            "Today is 2026-08-14. You are helping Sam in the \"Project Alpha\" space. "
            + "Every TODO tool acts only inside this space.");
    }

    /// <summary>
    /// The toolset is the same for every permission, so a Read member is told
    /// up front rather than discovering the limit from a refused write.
    /// </summary>
    [TestMethod]
    public async Task RunTellsAReadMemberTheSpaceIsReadOnly()
    {
        List<ChatMessage> seen = new List<ChatMessage>();
        Harness harness = new Harness(messages =>
        {
            seen.AddRange(messages);
            return ScriptedChatClient.Says("Sure.");
        });
        harness.GrantSpace(SpacePermission.Read);

        await harness.RunAsync(new AssistantTurn(TestTodo.SpaceId, "Hello.", null, null));

        seen[0].Text.Should().Be(
            "Today is 2026-08-14. You are helping Sam in the \"Project Alpha\" space. "
            + "Every TODO tool acts only inside this space. You have read-only access to "
            + "this space: you can list and look up TODOs but cannot create, change, or "
            + "delete them.");
    }

    [TestMethod]
    public async Task RunDoesNotRepeatTheReadOnlyNoticeForWriteMembers()
    {
        List<ChatMessage> seen = new List<ChatMessage>();
        Harness harness = new Harness(messages =>
        {
            seen.AddRange(messages);
            return ScriptedChatClient.Says("Sure.");
        });
        harness.GrantSpace(SpacePermission.Write);

        await harness.RunAsync(new AssistantTurn(TestTodo.SpaceId, "Hello.", null, null));

        seen[0].Text.Should().NotContain("read-only");
    }

    /// <summary>
    /// The Space check is the turn's first act. A user who cannot see the
    /// Space gets the same refusal any scoped request gets, before a
    /// confirmation is applied, before a client is built, and before a single
    /// event is published — so nothing is written and no model is called.
    /// </summary>
    [TestMethod]
    public async Task RunRefusesATurnInASpaceTheUserCannotSeeBeforeAnythingRuns()
    {
        Harness harness = new Harness(ScriptedChatClient.Says("Never sent."));
        harness.RevokeSpace();

        Func<Task> act = () => harness.RunAsync(new AssistantTurn(
            TestTodo.SpaceId,
            null,
            null,
            new ConfirmedAction(
                TodoToolNames.DeleteTodos,
                new[] { new TodoVersionReference(First, 5) })));

        await act.Should().ThrowAsync<NotFoundException>();
        harness.Events.Types().Should().BeEmpty();
        harness.Clients.DidNotReceiveWithAnyArgs().Create(default!);
        harness.Client!.ObservedOptions.Should().BeEmpty();
        await harness.Policy.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default);
    }

    /// <summary>
    /// The server keeps no history, so a write in a later turn depends on the
    /// echoed transcript still carrying what was read earlier.
    /// </summary>
    [TestMethod]
    public async Task RunRecoversEarlierReadsFromTheEchoedTranscript()
    {
        Harness harness = new Harness(
            ScriptedChatClient.Calls(
                TodoToolNames.ChangeTodoStatus,
                new Dictionary<string, object?>
                {
                    ["status"] = "Completed",
                    ["ids"] = new[] { First.ToString() },
                }));
        harness.Policy
            .ChangeStatusAsync(TestTodo.SpaceId, Arg.Any<TodoStatus>(), Arg.Any<IReadOnlyCollection<BulkTodoItemRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new BulkTodoResult(new[]
            {
                new BulkTodoResultItem(First, 12, TodoStatus.Completed, null, null),
            }));

        using JsonDocument earlier = JsonDocument.Parse(
            $$"""
            [
              {
                "role": "tool",
                "contents": [
                  {
                    "$type": "functionResult",
                    "callId": "1",
                    "result": { "items": [ { "id": "{{First}}", "version": 11 } ] }
                  }
                ]
              }
            ]
            """);

        await harness.RunAsync(new AssistantTurn(
            TestTodo.SpaceId,
            "Mark it done.",
            earlier.RootElement,
            null));

        await harness.Policy.Received(1).ChangeStatusAsync(
            TestTodo.SpaceId,
            TodoStatus.Completed,
            Arg.Is<IReadOnlyCollection<BulkTodoItemRequest>>(items =>
                items.Single().Version == 11),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The client echoes the transcript back each turn, so an unbounded
    /// conversation would grow the request body, the model's context, and the
    /// tokens every later turn pays to replay it. What is handed back is the
    /// windowed conversation, which is what stops the copy the client holds
    /// from growing.
    /// </summary>
    [TestMethod]
    public async Task RunReplaysAndHandsBackOnlyTheWindowedConversation()
    {
        List<ChatMessage> replayed = new List<ChatMessage>();
        Harness harness = new Harness(
            new AssistantOptions { TranscriptMaxMessages = 4 },
            messages =>
            {
                replayed.AddRange(messages);
                return ScriptedChatClient.Says("Noted.");
            });

        await harness.RunAsync(new AssistantTurn(TestTodo.SpaceId, "And now?", Exchanges(12), null));

        // Four from the window, plus the message this turn added.
        replayed.Should().HaveCount(5);
        replayed[0].Text.Should().Be("message 0");
        replayed[^1].Text.Should().Be("And now?");

        TurnTranscript? handedBack =
            harness.Events.Single<TurnTranscript>(TurnEventType.TurnCompleted);
        handedBack!.Messages.GetArrayLength().Should().BeLessThan(12);
    }

    /// <summary>
    /// The ledger is seeded from the windowed conversation, so a read that fell
    /// out of the window takes its version with it. The model cannot write
    /// against a version it can no longer see, which is the rule the browser
    /// holds and the reason writes are safe without trusting the model.
    /// </summary>
    [TestMethod]
    public async Task RunWillNotBindAVersionReadOutsideTheWindow()
    {
        Harness harness = new Harness(
            new AssistantOptions { TranscriptMaxMessages = 2 },
            _ => ScriptedChatClient.Calls(
                TodoToolNames.ChangeTodoStatus,
                new Dictionary<string, object?>
                {
                    ["status"] = "Completed",
                    ["ids"] = new[] { First.ToString() },
                }));

        using JsonDocument earlier = JsonDocument.Parse(
            $$"""
            [
              { "role": "user", "text": "message 0" },
              {
                "role": "tool",
                "contents": [
                  {
                    "$type": "functionResult",
                    "callId": "1",
                    "result": { "items": [ { "id": "{{First}}", "version": 11 } ] }
                  }
                ]
              },
              { "role": "user", "text": "message 2" },
              { "role": "assistant", "text": "message 3" }
            ]
            """);

        await harness.RunAsync(new AssistantTurn(TestTodo.SpaceId, "Mark it done.", earlier.RootElement, null));

        await harness.Policy.DidNotReceiveWithAnyArgs().ChangeStatusAsync(default, default, default!, default);
    }

    /// <summary>
    /// Written through the codec rather than by hand, so the transcript under
    /// test is the shape a real client echoes back.
    /// </summary>
    private static JsonElement Exchanges(int count)
    {
        return TranscriptCodec.Write(Enumerable
            .Range(0, count)
            .Select(index => new ChatMessage(
                index % 2 == 0 ? ChatRole.User : ChatRole.Assistant,
                $"message {index}"))
            .ToList());
    }

    private sealed class Harness
    {
        private static readonly AssistantConnection Configured = new AssistantConnection(
            AssistantProvider.Anthropic,
            "claude-sonnet-5",
            "sk-test",
            null,
            AssistantConnectionSource.User);

        private readonly IAssistantSettingsService settings =
            Substitute.For<IAssistantSettingsService>();

        private readonly IChatClientFactory clients = Substitute.For<IChatClientFactory>();

        public Harness(params Func<IEnumerable<ChatMessage>, ChatResponse>[] turns)
            : this(new AssistantOptions(), turns)
        {
        }

        public Harness(
            AssistantOptions options,
            params Func<IEnumerable<ChatMessage>, ChatResponse>[] turns)
            : this(Configured, options)
        {
            this.Client = new ScriptedChatClient(turns);
            this.clients.Create(Arg.Any<AssistantConnection>()).Returns(this.Client);
        }

        public Harness(params ChatResponse[] turns)
            : this(turns.Select(response =>
                new Func<IEnumerable<ChatMessage>, ChatResponse>(_ => response)).ToArray())
        {
        }

        public Harness(AssistantConnection? connection)
            : this(connection, new AssistantOptions())
        {
        }

        public Harness(AssistantConnection? connection, AssistantOptions options)
        {
            this.Sender = Substitute.For<ISender>();
            this.Policy = Substitute.For<IBulkConflictPolicy>();
            this.SpaceAccess = Substitute.For<ISpaceAccessService>();
            this.Events = new RecordingTurnEvents();
            this.settings.ResolveAsync(Arg.Any<CancellationToken>()).Returns(connection);
            this.GrantSpace(SpacePermission.Owner);

            IClock clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(TestTodo.Timestamp);

            this.Runner = new AssistantTurnRunner(
                this.settings,
                this.clients,
                this.Sender,
                this.Policy,
                new TestCurrentUser(),
                this.SpaceAccess,
                clock,
                NullLogger<TodoTools>.Instance,
                NullLogger<AssistantTurnRunner>.Instance,
                Options.Create(options));
        }

        public ISender Sender { get; } = null!;

        public IBulkConflictPolicy Policy { get; } = null!;

        public ISpaceAccessService SpaceAccess { get; } = null!;

        public IChatClientFactory Clients => this.clients;

        public RecordingTurnEvents Events { get; } = null!;

        public AssistantTurnRunner Runner { get; } = null!;

        public ScriptedChatClient? Client { get; }

        public Task RunAsync(AssistantTurn turn)
        {
            return this.Runner.RunAsync(turn, this.Events, CancellationToken.None);
        }

        public void StageSelection(params TodoDto[] found)
        {
            this.Sender
                .Send(Arg.Any<IRequest<TodoSelection>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new TodoSelection(found)));
        }

        /// <summary>
        /// The user holds <paramref name="permission"/> in the test Space, so a
        /// Read-level check passes and reports that level.
        /// </summary>
        public void GrantSpace(SpacePermission permission)
        {
            this.SpaceAccess
                .RequireAsync(TestTodo.SpaceId, SpacePermission.Read, Arg.Any<CancellationToken>())
                .Returns(new SpaceAccessContext(TestTodo.SpaceId, "Project Alpha", permission));
        }

        /// <summary>
        /// The user is not a member, so the check answers as it does for a
        /// Space that does not exist.
        /// </summary>
        public void RevokeSpace()
        {
            this.SpaceAccess
                .RequireAsync(TestTodo.SpaceId, Arg.Any<SpacePermission>(), Arg.Any<CancellationToken>())
                .Returns<SpaceAccessContext>(_ => throw new NotFoundException("Space", TestTodo.SpaceId));
        }
    }
}
