using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;
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
        [Description("Only TODOs with this status: NotStarted, InProgress, Completed, or Archived.")]
        string? status,
        [Description("Only TODOs with this priority: Low, Medium, or High.")]
        string? priority,
        [Description("Which shelf to read: Active (the default), Archived, or Deleted for the trash. Restoring needs Deleted.")]
        string? scope,
        [Description("Only TODOs due on or after this ISO date, such as 2026-08-14.")]
        string? dueFrom,
        [Description("Only TODOs due on or before this ISO date, such as 2026-08-14.")]
        string? dueTo,
        [Description("How many to return, at most 100. Defaults to 50.")]
        int? limit,
        CancellationToken cancellationToken)
    {
        TodoStatus? parsedStatus = null;
        TodoPriority? parsedPriority = null;
        TodoListScope parsedScope = TodoListScope.Active;
        DateOnly? parsedFrom = null;
        DateOnly? parsedTo = null;

        if (status is not null)
        {
            if (!TodoToolParsing.TryParseEnum(status, "status", out TodoStatus value, out string? error))
            {
                return new ToolFailure(error);
            }

            parsedStatus = value;
        }

        if (priority is not null)
        {
            if (!TodoToolParsing.TryParseEnum(priority, "priority", out TodoPriority value, out string? error))
            {
                return new ToolFailure(error);
            }

            parsedPriority = value;
        }

        if (scope is not null)
        {
            if (!TodoToolParsing.TryParseEnum(scope, "scope", out parsedScope, out string? error))
            {
                return new ToolFailure(error);
            }
        }

        if (dueFrom is not null)
        {
            if (!TodoToolParsing.TryParseDate(dueFrom, "dueFrom", out DateOnly value, out string? error))
            {
                return new ToolFailure(error);
            }

            parsedFrom = value;
        }

        if (dueTo is not null)
        {
            if (!TodoToolParsing.TryParseDate(dueTo, "dueTo", out DateOnly value, out string? error))
            {
                return new ToolFailure(error);
            }

            parsedTo = value;
        }

        CursorPage<TodoListItemDto> page = await this.DispatchAsync(
            new GetTodosQuery(
                parsedStatus,
                parsedPriority,
                parsedFrom,
                parsedTo,
                dependencyStatus: null,
                parsedScope,
                TodoSortField.DueDate,
                SortDirection.Asc,
                limit),
            cancellationToken);
        TodoSummary[] items = page.Items.Select(FromListItem).ToArray();
        this.ledger.RecordRange(items);

        return new TodoPage(items, page.NextCursor is not null);
    }

    public async Task<object> GetTodoSelectionAsync(
        [Description("The identifiers to look up, at most 100. Ones that no longer exist are left out of the answer rather than failing it.")]
        string[] ids,
        CancellationToken cancellationToken)
    {
        if (!TodoToolParsing.TryParseIds(ids, out Guid[]? parsed, out string? error))
        {
            return new ToolFailure(error);
        }

        if (Exceeds(parsed.Length, out string? capped))
        {
            return new ToolFailure(capped);
        }

        TodoSelection selection = await this.DispatchAsync(
            new GetTodoSelectionQuery(parsed),
            cancellationToken);
        TodoSummary[] items = selection.Items.Select(FromTodo).ToArray();
        this.ledger.RecordRange(items);

        return new TodoPage(items, HasMore: false);
    }

    public async Task<object> CreateTodoAsync(
        [Description("What the TODO is called.")]
        string name,
        [Description("Optional longer detail.")]
        string? description,
        [Description("When it is due, as an ISO date such as 2026-08-14.")]
        string dueDate,
        [Description("Low, Medium, or High.")]
        string priority,
        [Description("Set only for a repeating TODO: Daily, Weekly, Monthly, or Custom.")]
        string? recurrenceType,
        [Description("How many units between occurrences. Required with a Custom recurrence.")]
        int? recurrenceInterval,
        [Description("The unit a Custom recurrence counts in: Days, Weeks, or Months.")]
        string? recurrenceUnit,
        CancellationToken cancellationToken)
    {
        if (!TodoToolParsing.TryParseDate(dueDate, "dueDate", out DateOnly parsedDueDate, out string? error))
        {
            return new ToolFailure(error);
        }

        if (!TodoToolParsing.TryParseEnum(priority, "priority", out TodoPriority parsedPriority, out error))
        {
            return new ToolFailure(error);
        }

        RecurrenceType? parsedRecurrence = null;
        RecurrenceUnit? parsedUnit = null;

        if (recurrenceType is not null)
        {
            if (!TodoToolParsing.TryParseEnum(recurrenceType, "recurrenceType", out RecurrenceType value, out error))
            {
                return new ToolFailure(error);
            }

            parsedRecurrence = value;
        }

        if (recurrenceUnit is not null)
        {
            if (!TodoToolParsing.TryParseEnum(recurrenceUnit, "recurrenceUnit", out RecurrenceUnit value, out error))
            {
                return new ToolFailure(error);
            }

            parsedUnit = value;
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
        TodoSummary summary = FromTodo(created);
        this.ledger.Record(summary.Id, summary.Version);

        await this.ReportAsync(
            TodoToolNames.CreateTodo,
            $"Created '{created.Name}'.",
            new[] { created.Id },
            cancellationToken);

        return summary;
    }

    public async Task<object> ChangeTodoStatusAsync(
        [Description("The status to set: NotStarted, InProgress, Completed, or Archived.")]
        string status,
        [Description("The TODOs to change, at most 100, all of which must have been read in this conversation.")]
        string[] ids,
        CancellationToken cancellationToken)
    {
        if (!TodoToolParsing.TryParseEnum(status, "status", out TodoStatus parsedStatus, out string? error))
        {
            return new ToolFailure(error);
        }

        if (!this.TryBind(ids, out IReadOnlyCollection<BulkTodoItemRequest>? items, out error))
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
        CancellationToken cancellationToken)
    {
        if (!TodoToolParsing.TryParseIds(ids, out Guid[]? parsed, out string? error))
        {
            return new ToolFailure(error);
        }

        if (Exceeds(parsed.Length, out string? capped))
        {
            return new ToolFailure(capped);
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

        ConfirmationItem[] items = selection.Items
            .Select(todo => new ConfirmationItem(
                todo.Id,
                todo.Name,
                todo.Version,
                todo.Status,
                todo.DeletedAt))
            .ToArray();

        await this.events.PublishAsync(
            TurnEvent.ConfirmationRequired(new ConfirmationRequest(
                TodoToolNames.DeleteTodos,
                $"Delete {items.Length} TODO{(items.Length == 1 ? string.Empty : "s")}?",
                items)),
            cancellationToken);
        this.controller.Halt();

        return new ToolFailure(
            "Deletion needs the user's confirmation. They have been asked; "
            + "stop here and wait for their answer.");
    }

    public async Task<object> RestoreTodosAsync(
        [Description("The deleted TODOs to restore, at most 100, all of which must have been read in this conversation with scope Deleted.")]
        string[] ids,
        CancellationToken cancellationToken)
    {
        if (!this.TryBind(ids, out IReadOnlyCollection<BulkTodoItemRequest>? items, out string? error))
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

        BulkTodoItemRequest[] items = confirmation.Items
            .Select(item => new BulkTodoItemRequest(item.Id, item.Version))
            .ToArray();
        BulkTodoResult result = await this.policy.DeleteAsync(items, cancellationToken);

        return await this.ReportWriteAsync(
            TodoToolNames.DeleteTodos,
            $"Deleted {result.Items.Count}.",
            items,
            result,
            cancellationToken);
    }

    private static TodoSummary FromListItem(TodoListItemDto item)
    {
        return new TodoSummary(
            item.Id,
            item.Name,
            item.Version,
            item.DueDate,
            item.Status.ToString(),
            item.Priority.ToString(),
            item.DeletedAt is not null,
            item.IsBlocked);
    }

    private static TodoSummary FromTodo(TodoDto todo)
    {
        return new TodoSummary(
            todo.Id,
            todo.Name,
            todo.Version,
            todo.DueDate,
            todo.Status.ToString(),
            todo.Priority.ToString(),
            todo.DeletedAt is not null,
            IsBlocked: null);
    }

    /// <summary>
    /// Refuses rather than chunks. Splitting would abandon the all-or-nothing
    /// guarantee, and the assistant could not then describe honestly what
    /// actually happened.
    /// </summary>
    private static bool Exceeds(int count, [NotNullWhen(true)] out string? error)
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

    private bool TryBind(
        string[] ids,
        out IReadOnlyCollection<BulkTodoItemRequest> items,
        [NotNullWhen(false)] out string? error)
    {
        items = Array.Empty<BulkTodoItemRequest>();

        if (!TodoToolParsing.TryParseIds(ids, out Guid[]? parsed, out error))
        {
            return false;
        }

        if (Exceeds(parsed.Length, out error))
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

    private async Task<object> ReportWriteAsync(
        string tool,
        string summary,
        IReadOnlyCollection<BulkTodoItemRequest> sent,
        BulkTodoResult result,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, long> before = sent.ToDictionary(item => item.Id, item => item.Version);
        int changed = result.Items.Count(item =>
            !before.TryGetValue(item.Id, out long version) || version != item.Version);

        foreach (BulkTodoResultItem item in result.Items)
        {
            this.ledger.Record(item.Id, item.Version);
        }

        // A recurring completion creates an occurrence nobody named, so the
        // identifiers reported are the ones written plus anything they spawned.
        Guid[] touched = result.Items
            .Select(item => item.Id)
            .Concat(result.Items
                .Where(item => item.NextOccurrenceId is not null)
                .Select(item => item.NextOccurrenceId!.Value))
            .ToArray();

        await this.ReportAsync(tool, summary, touched, cancellationToken);

        TodoSummary[] items = result.Items
            .Select(item => new TodoSummary(
                item.Id,
                Name: string.Empty,
                item.Version,
                default,
                item.Status.ToString(),
                Priority: string.Empty,
                item.DeletedAt is not null,
                IsBlocked: null))
            .ToArray();

        return new TodoWriteOutcome(changed, result.Items.Count - changed, items);
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

    private async Task<TResponse> DispatchAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        using IDisposable? origin = AssistantOrigin.Begin(this.logger);

        return await this.sender.Send(request, cancellationToken);
    }
}
