using System.Globalization;
using System.Text.Json;

using MediatR;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Assistant.Conflicts;
using Sleeky.Todo.Assistant.Providers;
using Sleeky.Todo.Assistant.Tools;

namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// Runs one turn: resolve a provider, replay the conversation, let the model
/// work, and hand the conversation back.
/// </summary>
public sealed class AssistantTurnRunner : IAssistantTurnRunner
{
    private const string NotConfigured =
        "No AI provider is set up yet. Add a provider, model, and API key in "
        + "assistant settings, and I can start helping with your TODOs.";

    private const int TurnCompletedEventId = 4001;

    private const string TurnCompletedMessage =
        "Assistant turn completed with {ToolCalls} tool calls, "
        + "{InputTokens} input and {OutputTokens} output tokens";

    private readonly IAssistantSettingsService settings;

    private readonly IChatClientFactory clients;

    private readonly ISender sender;

    private readonly IBulkConflictPolicy policy;

    private readonly ICurrentUser currentUser;

    private readonly IClock clock;

    private readonly ILogger<TodoTools> toolLogger;

    private readonly ILogger<AssistantTurnRunner> logger;

    private readonly AssistantOptions options;

    public AssistantTurnRunner(
        IAssistantSettingsService settings,
        IChatClientFactory clients,
        ISender sender,
        IBulkConflictPolicy policy,
        ICurrentUser currentUser,
        IClock clock,
        ILogger<TodoTools> toolLogger,
        ILogger<AssistantTurnRunner> logger,
        IOptions<AssistantOptions> options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(toolLogger);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        this.settings = settings;
        this.clients = clients;
        this.sender = sender;
        this.policy = policy;
        this.currentUser = currentUser;
        this.clock = clock;
        this.toolLogger = toolLogger;
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task RunAsync(
        AssistantTurn turn,
        ITurnEventWriter events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(events);

        await events.PublishAsync(TurnEvent.TurnStarted(), cancellationToken);

        AssistantConnection? connection = await this.settings.ResolveAsync(cancellationToken);

        if (connection is null)
        {
            await HandBackUnansweredAsync(turn, events, cancellationToken);
            return;
        }

        List<ChatMessage> messages = TranscriptCodec.Read(turn.Transcript);
        bool trimmed = TranscriptWindow.Apply(messages, this.options.TranscriptMaxMessages);
        TodoVersionLedger ledger = SeedLedgerFromWindow(turn.Transcript, messages, trimmed);
        TodoTools tools = this.CreateTools(ledger, events);

        await this.AppendTurnInputAsync(messages, turn, tools, cancellationToken);

        ChatResponse response = await this.AskModelAsync(connection, messages, tools, cancellationToken);
        messages.AddRange(response.Messages);
        this.LogTurnCost(response);

        await PublishOutcomeAsync(response, messages, events, cancellationToken);
    }

    /// <summary>
    /// Says there is nothing to run on, and hands the conversation back
    /// untouched rather than cleared. Losing a provider mid-session is
    /// recoverable, and emptying the transcript would silently discard the
    /// exchange the user is still looking at.
    /// </summary>
    private static async Task HandBackUnansweredAsync(
        AssistantTurn turn,
        ITurnEventWriter events,
        CancellationToken cancellationToken)
    {
        await events.PublishAsync(
            TurnEvent.Message(new AssistantMessage(NotConfigured)),
            cancellationToken);
        await events.PublishAsync(
            TurnEvent.TurnCompleted(new TurnTranscript(
                turn.Transcript ?? TranscriptCodec.Empty())),
            cancellationToken);
    }

    /// <summary>
    /// Seeds the ledger from what is left once the window has been applied, so
    /// a version can only be bound to a read the model can still see.
    /// </summary>
    /// <remarks>
    /// Nothing is re-serialized when nothing was dropped, which also keeps the
    /// seed reading the transcript that arrived rather than one this version
    /// was able to deserialize.
    /// </remarks>
    private static TodoVersionLedger SeedLedgerFromWindow(
        JsonElement? transcript,
        IReadOnlyList<ChatMessage> messages,
        bool trimmed)
    {
        TodoVersionLedger ledger = new TodoVersionLedger();
        TranscriptCodec.SeedLedger(
            trimmed ? TranscriptCodec.Write(messages) : transcript,
            ledger);

        return ledger;
    }

    /// <summary>
    /// The model's answer, when it gave one, and then the conversation as it
    /// now stands. A turn a tool halted — a deletion proposal — may end
    /// without an answer; the transcript still goes back so the next turn
    /// continues from here.
    /// </summary>
    private static async Task PublishOutcomeAsync(
        ChatResponse response,
        IReadOnlyList<ChatMessage> messages,
        ITurnEventWriter events,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            await events.PublishAsync(
                TurnEvent.Message(new AssistantMessage(response.Text)),
                cancellationToken);
        }

        await events.PublishAsync(
            TurnEvent.TurnCompleted(new TurnTranscript(TranscriptCodec.Write(messages))),
            cancellationToken);
    }

    private TodoTools CreateTools(TodoVersionLedger ledger, ITurnEventWriter events)
    {
        return new TodoTools(
            this.sender,
            this.policy,
            ledger,
            events,
            new FunctionInvocationTurnController(),
            this.toolLogger);
    }

    /// <summary>
    /// Adds what this turn brings: the opening context when the conversation
    /// is new, the outcome of a confirmation the user answered, and whatever
    /// they typed.
    /// </summary>
    private async Task AppendTurnInputAsync(
        List<ChatMessage> messages,
        AssistantTurn turn,
        TodoTools tools,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            messages.Add(new ChatMessage(ChatRole.User, this.DescribeContext()));
        }

        if (turn.Confirmation is not null)
        {
            messages.Add(new ChatMessage(
                ChatRole.User,
                await this.ApplyConfirmationAsync(turn.Confirmation, tools, cancellationToken)));
        }

        if (!string.IsNullOrWhiteSpace(turn.Message))
        {
            messages.Add(new ChatMessage(ChatRole.User, turn.Message));
        }
    }

    /// <summary>
    /// One request to the model, with the loop running its tool calls until it
    /// answers or a tool halts the turn.
    /// </summary>
    private async Task<ChatResponse> AskModelAsync(
        AssistantConnection connection,
        List<ChatMessage> messages,
        TodoTools tools,
        CancellationToken cancellationToken)
    {
        using IChatClient client = new ChatClientBuilder(this.clients.Create(connection))
            .UseFunctionInvocation()
            .Build();

        return await client.GetResponseAsync(
            messages,
            new ChatOptions
            {
                Instructions = AssistantSystemPrompt.Text,

                // Identical on every request. A tool list that varied per turn
                // would move the prefix and defeat caching wherever a provider
                // offers it.
                Tools = TodoToolset.Create(tools).ToList(),
            },
            cancellationToken);
    }

    /// <summary>
    /// What the turn cost, on one line.
    /// </summary>
    /// <remarks>
    /// Not a spending record — the key is the user's own, so the provider bills
    /// them directly. This is the number that separates a slow turn spent
    /// waiting on the model from one spent in the tools, which the transcript
    /// alone cannot tell you. Counts are whatever the provider reported, so a
    /// provider that reports nothing logs nothing rather than a fabricated zero.
    /// </remarks>
    private void LogTurnCost(ChatResponse response)
    {
        int toolCalls = response.Messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Count();

        this.logger.LogInformation(
            TurnCompletedEventId,
            TurnCompletedMessage,
            toolCalls,
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount);
    }

    /// <summary>
    /// Runs the deletion the user agreed to, and reports the outcome to the
    /// model as something that has already happened.
    /// </summary>
    /// <remarks>
    /// The model is not asked to propose it again. They confirmed a specific
    /// selection at specific versions, so re-deciding here would put the
    /// deletion back in the hands of the thing the gate exists to check.
    /// </remarks>
    private async Task<string> ApplyConfirmationAsync(
        ConfirmedAction confirmation,
        TodoTools tools,
        CancellationToken cancellationToken)
    {
        // The tool name decides what runs. Deletion is the only tool that
        // proposes, so anything else naming itself here is a client that has
        // reused this path — and executing a deletion for it would invert the
        // very intent the gate exists to check.
        if (!string.Equals(
            confirmation.Tool,
            TodoToolNames.DeleteTodos,
            StringComparison.Ordinal))
        {
            return $"I answered a '{confirmation.Tool}' confirmation, which is not "
                + "something you can ask me to confirm. Nothing was changed. Tell me "
                + "what you were trying to do.";
        }

        object outcome = await tools.ExecuteConfirmedDeletionAsync(confirmation, cancellationToken);

        if (outcome is ToolFailure failure)
        {
            return "I answered a deletion confirmation, but it could not be applied: "
                + failure.Error
                + " Nothing was changed.";
        }

        return "I confirmed the deletion. It has been applied; here is what "
            + "happened, which you should summarise for me: "
            + JsonSerializer.Serialize(outcome, AIJsonUtilities.DefaultOptions);
    }

    /// <summary>
    /// The conversation's opening context. It is a user message rather than
    /// part of the system prompt so the cacheable prefix stays still, and it is
    /// written once so it stays identical for the life of the conversation.
    /// </summary>
    private string DescribeContext()
    {
        string today = DateOnly.FromDateTime(this.clock.UtcNow.UtcDateTime)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string name = string.IsNullOrWhiteSpace(this.currentUser.DisplayName)
            ? "the user"
            : this.currentUser.DisplayName;

        return $"Today is {today}. You are helping {name} with their TODO list.";
    }
}
