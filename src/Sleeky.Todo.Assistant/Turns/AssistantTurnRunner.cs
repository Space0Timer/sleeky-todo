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

    private readonly IAssistantSettingsService settings;

    private readonly IChatClientFactory clients;

    private readonly ISender sender;

    private readonly IBulkConflictPolicy policy;

    private readonly ICurrentUser currentUser;

    private readonly IClock clock;

    private readonly ILogger<TodoTools> toolLogger;

    private readonly AssistantOptions options;

    public AssistantTurnRunner(
        IAssistantSettingsService settings,
        IChatClientFactory clients,
        ISender sender,
        IBulkConflictPolicy policy,
        ICurrentUser currentUser,
        IClock clock,
        ILogger<TodoTools> toolLogger,
        IOptions<AssistantOptions> options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(toolLogger);
        ArgumentNullException.ThrowIfNull(options);

        this.settings = settings;
        this.clients = clients;
        this.sender = sender;
        this.policy = policy;
        this.currentUser = currentUser;
        this.clock = clock;
        this.toolLogger = toolLogger;
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
            await events.PublishAsync(
                TurnEvent.Message(new AssistantMessage(NotConfigured)),
                cancellationToken);

            // The conversation is handed back untouched rather than cleared.
            // Losing a provider mid-session is recoverable, and emptying the
            // transcript would silently discard the exchange the user is still
            // looking at.
            await events.PublishAsync(
                TurnEvent.TurnCompleted(new TurnTranscript(
                    turn.Transcript ?? TranscriptCodec.Empty())),
                cancellationToken);
            return;
        }

        List<ChatMessage> messages = TranscriptCodec.Read(turn.Transcript);
        bool trimmed = TranscriptWindow.Apply(messages, this.options.TranscriptMaxMessages);

        TodoVersionLedger ledger = new TodoVersionLedger();

        // Seeded from what is left once the window has been applied, so a
        // version can only be bound to a read the model can still see. Nothing
        // is re-serialized when nothing was dropped, which also keeps the seed
        // reading the transcript that arrived rather than one this version was
        // able to deserialize.
        TranscriptCodec.SeedLedger(
            trimmed ? TranscriptCodec.Write(messages) : turn.Transcript,
            ledger);

        TodoTools tools = new TodoTools(
            this.sender,
            this.policy,
            ledger,
            events,
            new FunctionInvocationTurnController(),
            this.toolLogger);

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

        using IChatClient client = new ChatClientBuilder(this.clients.Create(connection))
            .UseFunctionInvocation()
            .Build();
        ChatResponse response = await client.GetResponseAsync(
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

        messages.AddRange(response.Messages);

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
