using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;
using Sleeky.Todo.Application.Todos.Validation;
using Sleeky.Todo.Assistant.Conflicts;
using Sleeky.Todo.Assistant.Turns;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// The six operations the assistant can perform, each a thin pass over a
/// command or query.
/// </summary>
/// <remarks>
/// Thin is the point. Every call goes through MediatR, so it inherits
/// validation, domain-rule translation, request logging, and owner scoping —
/// every guardrail the HTTP API has. Nothing here reaches a repository, so
/// there is no path by which the assistant can do something a browser could
/// not.
///
/// Every tool follows the same shape: parse what the model sent, refuse with a
/// <see cref="ToolFailure"/> the model can act on, dispatch, record what the
/// model now knows in the ledger, and report. A parameter the tool checks
/// itself is one whose failure would otherwise surface as a thrown exception,
/// which the loop reports to the model as a generic error naming nothing — so
/// it would retry the same call until the turn aborts.
/// </remarks>
public sealed class TodoTools
{
    private readonly ISender sender;

    private readonly IBulkConflictPolicy policy;

    private readonly TodoVersionLedger ledger;

    private readonly ITurnEventWriter events;

    private readonly ITurnController controller;

    private readonly ILogger<TodoTools> logger;

    public TodoTools(
        ISender sender,
        IBulkConflictPolicy policy,
        TodoVersionLedger ledger,
        ITurnEventWriter events,
        ITurnController controller,
        ILogger<TodoTools> logger)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(logger);

        this.sender = sender;
        this.policy = policy;
        this.ledger = ledger;
        this.events = events;
        this.controller = controller;
        this.logger = logger;
    }

    public async Task<object> GetTodosAsync(
        [Description("Only TODOs with this status: Open, InProgress, Completed, or Archived.")]
        string? status = null,
        [Description("Only TODOs with this priority: Low, Medium, or High.")]
        string? priority = null,
        [Description("Which shelf to read: Active (the default), Archived, or Deleted for the trash. Restoring needs Deleted.")]
        string? scope = null,
        [Description("Only TODOs due on or after this ISO date, such as 2026-08-14.")]
        string? dueFrom = null,
        [Description("Only TODOs due on or before this ISO date, such as 2026-08-14.")]
        string? dueTo = null,
        [Description("Only TODOs whose name or description contains these words, at most 200 characters. Each word matches from the start of a word, so \"quart\" finds \"quarterly\" but \"uarter\" finds nothing, and every word given must match.")]
        string? search = null,
        [Description("How many to return, at most 100. Defaults to 50.")]
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (!TodoToolParsing.TryParseOptionalEnum(status, "status", out TodoStatus? parsedStatus, out string? error))
        {
            return new ToolFailure(error);
        }

        if (!TodoToolParsing.TryParseOptionalEnum(priority, "priority", out TodoPriority? parsedPriority, out error))
        {
            return new ToolFailure(error);
        }

        if (!TodoToolParsing.TryParseOptionalEnum(scope, "scope", out TodoListScope? parsedScope, out error))
        {
            return new ToolFailure(error);
        }

        if (!TodoToolParsing.TryParseOptionalDate(dueFrom, "dueFrom", out DateOnly? parsedFrom, out error))
        {
            return new ToolFailure(error);
        }

        if (!TodoToolParsing.TryParseOptionalDate(dueTo, "dueTo", out DateOnly? parsedTo, out error))
        {
            return new ToolFailure(error);
        }

        if (!TryCheckListBounds(search, limit, out error))
        {
            return new ToolFailure(error);
        }

        CursorPage<TodoListItemDto> page = await this.DispatchAsync(
            new GetTodosQuery(
                parsedStatus,
                parsedPriority,
                parsedFrom,
                parsedTo,
                dependencyStatus: null,
                parsedScope ?? TodoListScope.Active,
                TodoSortField.DueDate,
                SortDirection.Asc,
                limit,
                cursor: null,
                search),
            cancellationToken);

        return this.RecordRead(
            page.Items.Select(TodoSummary.FromListItem),
            hasMore: page.NextCursor is not null);
    }

    public async Task<object> GetTodoSelectionAsync(
        [Description("The identifiers to look up, at most 100. Ones that no longer exist are left out of the answer rather than failing it.")]
        string[] ids,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseSelection(ids, out Guid[]? parsed, out string? error))
        {
            return new ToolFailure(error);
        }

        TodoSelection selection = await this.DispatchAsync(
            new GetTodoSelectionQuery(parsed),
            cancellationToken);

        return this.RecordRead(selection.Items.Select(TodoSummary.FromTodo), hasMore: false);
    }

    public async Task<object> CreateTodoAsync(
        [Description("What the TODO is called.")]
        string name,
        [Description("When it is due, as an ISO date such as 2026-08-14.")]
        string dueDate,
        [Description("Low, Medium, or High.")]
        string priority,
        [Description("Optional longer detail.")]
        string? description = null,
        [Description("Set only for a repeating TODO: Daily, Weekly, Monthly, or Custom.")]
        string? recurrenceType = null,
        [Description("How many units between occurrences. Required with a Custom recurrence.")]
        int? recurrenceInterval = null,
        [Description("The unit a Custom recurrence counts in: Days, Weeks, or Months.")]
        string? recurrenceUnit = null,
        CancellationToken cancellationToken = default)
    {
        if (!TodoToolParsing.TryParseDate(dueDate, "dueDate", out DateOnly parsedDueDate, out string? error))
        {
            return new ToolFailure(error);
        }

        if (!TodoToolParsing.TryParseEnum(priority, "priority", out TodoPriority parsedPriority, out error))
        {
            return new ToolFailure(error);
        }

        if (!TodoToolParsing.TryParseOptionalEnum(recurrenceType, "recurrenceType", out RecurrenceType? parsedRecurrence, out error))
        {
            return new ToolFailure(error);
        }

        if (!TodoToolParsing.TryParseOptionalEnum(recurrenceUnit, "recurrenceUnit", out RecurrenceUnit? parsedUnit, out error))
        {
            return new ToolFailure(error);
        }

        TodoDto created = await this.DispatchAsync(
            new CreateTodoCommand(
                name,
                description,
                parsedDueDate,
                parsedPriority,
                parsedRecurrence,
                recurrenceInterval,
                parsedUnit),
            cancellationToken);
        TodoSummary summary = TodoSummary.FromTodo(created);
        this.ledger.Record(summary.Id, summary.Version);

        await this.ReportAsync(
            TodoToolNames.CreateTodo,
            $"Created '{created.Name}'.",
            new[] { created.Id },
            cancellationToken);

        return summary;
    }

    public async Task<object> ChangeTodoStatusAsync(
        [Description("The status to set: Open, InProgress, Completed, or Archived.")]
        string status,
        [Description("The TODOs to change, at most 100, all of which must have been read in this conversation.")]
        string[] ids,
        CancellationToken cancellationToken = default)
    {
        if (!TodoToolParsing.TryParseEnum(status, "status", out TodoStatus parsedStatus, out string? error))
        {
            return new ToolFailure(error);
        }

        if (!this.TryBindLastReadVersions(ids, out IReadOnlyCollection<BulkTodoItemRequest>? items, out error))
        {
            return new ToolFailure(error);
        }

        BulkTodoResult result = await this.policy.ChangeStatusAsync(
            parsedStatus,
            items,
            cancellationToken);

        return await this.ReportWriteAsync(
            TodoToolNames.ChangeTodoStatus,
            $"Set {result.Items.Count} to {parsedStatus}.",
            items,
            result,
            cancellationToken);
    }

    /// <summary>
    /// Proposes a deletion and stops the turn. It never deletes: the confirming
    /// turn does, with the versions this proposal displayed.
    /// </summary>
    public async Task<object> DeleteTodosAsync(
        [Description("The TODOs to propose deleting, at most 100.")]
        string[] ids,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseSelection(ids, out Guid[]? parsed, out string? error))
        {
            return new ToolFailure(error);
        }

        // Read here rather than trusting the ledger, because this state is what
        // a person is about to answer for. It is also what the confirming turn
        // sends, so what they saw is what gets written.
        TodoSelection selection = await this.DispatchAsync(
            new GetTodoSelectionQuery(parsed),
            cancellationToken);

        if (selection.Items.Count != parsed.Length)
        {
            return new ToolFailure(
                "Some of those TODOs no longer exist. Read them again before deleting.");
        }

        await this.AskToConfirmDeletionAsync(selection, cancellationToken);
        this.controller.Halt();

        return new ToolFailure(
            "Deletion needs the user's confirmation. They have been asked; "
            + "stop here and wait for their answer.");
    }

    public async Task<object> RestoreTodosAsync(
        [Description("The deleted TODOs to restore, at most 100, all of which must have been read in this conversation with scope Deleted.")]
        string[] ids,
        CancellationToken cancellationToken = default)
    {
        if (!this.TryBindLastReadVersions(ids, out IReadOnlyCollection<BulkTodoItemRequest>? items, out string? error))
        {
            return new ToolFailure(error);
        }

        BulkTodoResult result = await this.policy.RestoreAsync(items, cancellationToken);

        return await this.ReportWriteAsync(
            TodoToolNames.RestoreTodos,
            $"Restored {result.Items.Count}.",
            items,
            result,
            cancellationToken);
    }

    /// <summary>
    /// Runs a deletion a person has agreed to, with the versions they were
    /// shown. The model is not consulted again: they confirmed this selection,
    /// not a fresh proposal.
    /// </summary>
    public async Task<object> ExecuteConfirmedDeletionAsync(
        ConfirmedAction confirmation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        // Checked here rather than left to the command's validator, because
        // this runs outside the tool loop: a validation failure would leave the
        // response as a stream that stops mid-turn rather than as something the
        // model can explain.
        if (!TryReadConfirmed(confirmation, out BulkTodoItemRequest[] items, out string? error))
        {
            return new ToolFailure(error);
        }

        BulkTodoResult result = await this.policy.DeleteAsync(items, cancellationToken);

        return await this.ReportWriteAsync(
            TodoToolNames.DeleteTodos,
            $"Deleted {result.Items.Count}.",
            items,
            result,
            cancellationToken);
    }

    /// <summary>
    /// The two bounded list parameters, checked here rather than left to the
    /// query's validator, which would throw. The search text is trimmed before
    /// measuring because the query trims before validating, and a limit that
    /// rejected what the server would have accepted would send the model
    /// looking for a fault that is not there.
    /// </summary>
    private static bool TryCheckListBounds(
        string? search,
        int? limit,
        [NotNullWhen(false)] out string? error)
    {
        if (search is not null
            && search.Trim().Length > TodoValidationLimits.SearchTextMaximumLength)
        {
            error = $"search must not exceed {TodoValidationLimits.SearchTextMaximumLength} characters.";
            return false;
        }

        if (limit is < 1 or > GetTodosQuery.MaximumPageSize)
        {
            error = $"limit must be between 1 and {GetTodosQuery.MaximumPageSize}.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Parses a batch of identifiers and holds it to the batch cap.
    /// </summary>
    private static bool TryParseSelection(
        string[] ids,
        [NotNullWhen(true)] out Guid[]? parsed,
        [NotNullWhen(false)] out string? error)
    {
        if (!TodoToolParsing.TryParseIds(ids, out parsed, out error))
        {
            return false;
        }

        return !ExceedsBatchCap(parsed.Length, out error);
    }

    /// <summary>
    /// Refuses rather than chunks. Splitting would abandon the all-or-nothing
    /// guarantee, and the assistant could not then describe honestly what
    /// actually happened.
    /// </summary>
    private static bool ExceedsBatchCap(int count, [NotNullWhen(true)] out string? error)
    {
        if (count <= BulkTodoLimits.MaximumSelectionSize)
        {
            error = null;
            return false;
        }

        error = $"That is {count} TODOs, and a batch is capped at "
            + $"{BulkTodoLimits.MaximumSelectionSize}. Narrow the selection and "
            + "ask the user which ones they mean, rather than splitting it up.";
        return true;
    }

    /// <summary>
    /// Applies to a confirmed selection the same checks a proposed one gets.
    /// The items come from a client rather than from the model, so they arrive
    /// having passed through no tool schema at all.
    /// </summary>
    private static bool TryReadConfirmed(
        ConfirmedAction confirmation,
        out BulkTodoItemRequest[] items,
        [NotNullWhen(false)] out string? error)
    {
        items = Array.Empty<BulkTodoItemRequest>();

        if (confirmation.Items.Count == 0)
        {
            error = "That confirmation named no TODOs, so nothing was deleted.";
            return false;
        }

        if (ExceedsBatchCap(confirmation.Items.Count, out error))
        {
            return false;
        }

        if (confirmation.Items.Any(item => item.Id == Guid.Empty || item.Version <= 0))
        {
            error = "That confirmation carried an unusable identifier or version, "
                + "so nothing was deleted.";
            return false;
        }

        if (confirmation.Items.Select(item => item.Id).Distinct().Count()
            != confirmation.Items.Count)
        {
            error = "That confirmation named the same TODO more than once, so "
                + "nothing was deleted.";
            return false;
        }

        items = confirmation.Items
            .Select(item => new BulkTodoItemRequest(item.Id, item.Version))
            .ToArray();
        error = null;
        return true;
    }

    /// <summary>
    /// The written identifiers plus anything they spawned. A recurring
    /// completion creates an occurrence nobody named, and the client needs to
    /// know to look at it.
    /// </summary>
    private static Guid[] CollectTouchedIds(BulkTodoResult result)
    {
        return result.Items
            .Select(item => item.Id)
            .Concat(result.Items
                .Where(item => item.NextOccurrenceId is not null)
                .Select(item => item.NextOccurrenceId!.Value))
            .ToArray();
    }

    /// <summary>
    /// How many items the write actually moved. An item whose version came back
    /// unchanged was already in the requested state, and the outcome says so
    /// rather than letting the model claim it changed something it did not.
    /// </summary>
    private static int CountChanged(
        IReadOnlyCollection<BulkTodoItemRequest> sent,
        BulkTodoResult result)
    {
        Dictionary<Guid, long> before = sent.ToDictionary(item => item.Id, item => item.Version);

        return result.Items.Count(item =>
            !before.TryGetValue(item.Id, out long version) || version != item.Version);
    }

    /// <summary>
    /// Binds a batch of identifiers to the versions the model last read them
    /// at. Anything it has not read in this conversation is refused with an
    /// instruction to read first, rather than written against a version fetched
    /// behind its back.
    /// </summary>
    private bool TryBindLastReadVersions(
        string[] ids,
        out IReadOnlyCollection<BulkTodoItemRequest> items,
        [NotNullWhen(false)] out string? error)
    {
        items = Array.Empty<BulkTodoItemRequest>();

        if (!TryParseSelection(ids, out Guid[]? parsed, out error))
        {
            return false;
        }

        if (!this.ledger.TryBind(parsed, out items, out IReadOnlyCollection<Guid> unread))
        {
            error = "These have not been read in this conversation, so there is "
                + "no version to write against: "
                + string.Join(", ", unread)
                + ". Read them first.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Every read passes through here, so the ledger sees every version the
    /// model does and a later write can bind to it.
    /// </summary>
    private TodoPage RecordRead(IEnumerable<TodoSummary> summaries, bool hasMore)
    {
        TodoSummary[] items = summaries.ToArray();
        this.ledger.RecordRange(items);

        return new TodoPage(items, hasMore);
    }

    /// <summary>
    /// Publishes the proposal a person will answer, with the selection as it
    /// stood when it was read.
    /// </summary>
    private async Task AskToConfirmDeletionAsync(
        TodoSelection selection,
        CancellationToken cancellationToken)
    {
        ConfirmationItem[] items = selection.Items
            .Select(todo => new ConfirmationItem(
                todo.Id,
                todo.Name,
                todo.Version,
                todo.Status,
                todo.DeletedAt))
            .ToArray();
        string prompt = $"Delete {items.Length} TODO{(items.Length == 1 ? string.Empty : "s")}?";

        await this.events.PublishAsync(
            TurnEvent.ConfirmationRequired(new ConfirmationRequest(
                TodoToolNames.DeleteTodos,
                prompt,
                items)),
            cancellationToken);
    }

    /// <summary>
    /// Closes out a write: the ledger learns the versions the write produced,
    /// the client is told what to refresh, and the model gets an outcome that
    /// separates what changed from what was already so.
    /// </summary>
    private async Task<object> ReportWriteAsync(
        string tool,
        string summary,
        IReadOnlyCollection<BulkTodoItemRequest> sent,
        BulkTodoResult result,
        CancellationToken cancellationToken)
    {
        int changed = CountChanged(sent, result);
        this.RecordWrittenVersions(result);

        await this.ReportAsync(tool, summary, CollectTouchedIds(result), cancellationToken);

        TodoSummary[] items = result.Items.Select(TodoSummary.FromWriteResult).ToArray();

        return new TodoWriteOutcome(changed, result.Items.Count - changed, items);
    }

    /// <summary>
    /// A write advances versions, and the model may write to the same items
    /// again in this turn without re-reading them.
    /// </summary>
    private void RecordWrittenVersions(BulkTodoResult result)
    {
        foreach (BulkTodoResultItem item in result.Items)
        {
            this.ledger.Record(item.Id, item.Version);
        }
    }

    private async Task ReportAsync(
        string tool,
        string summary,
        IReadOnlyCollection<Guid> touched,
        CancellationToken cancellationToken)
    {
        await this.events.PublishAsync(
            TurnEvent.ToolExecuted(new ToolExecution(tool, summary, Succeeded: true)),
            cancellationToken);
        await this.events.PublishAsync(
            TurnEvent.TodosChanged(new TodoChangeNotice(touched)),
            cancellationToken);
    }

    /// <summary>
    /// Sends through MediatR under the assistant's origin marker, so the request
    /// log can tell the assistant's commands from the person's own.
    /// </summary>
    private async Task<TResponse> DispatchAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        using IDisposable? origin = AssistantOrigin.Begin(this.logger);

        return await this.sender.Send(request, cancellationToken);
    }
}
