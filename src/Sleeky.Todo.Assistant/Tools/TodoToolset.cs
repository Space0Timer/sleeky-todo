using Microsoft.Extensions.AI;

using Sleeky.Todo.Application.Todos.Commands.Bulk;

namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// Presents the six operations to a model.
/// </summary>
/// <remarks>
/// Every description names the condition that should trigger the call rather
/// than only describing what the tool does. That is good practice on any
/// provider and it measurably lifts correct tool selection on the smaller
/// models, which under-reach for tools when a description reads as a label.
///
/// The set is identical on every request. A tool list that varied per turn
/// would move the prefix and defeat caching wherever a provider offers it.
/// </remarks>
public static class TodoToolset
{
    private const string GetTodosDescription =
        "Read the user's TODOs. Call this whenever a request names TODOs by "
        + "description rather than by identifier — \"everything due this week\", "
        + "\"the high-priority ones\", \"what's in the trash\" — because the "
        + "answer carries the identifiers and versions every other tool needs. "
        + "When the request names them by what they are about — \"the ones about "
        + "the tax return\" — pass those words as search rather than reading the "
        + "whole list and picking. Returns hasMore when the list is longer than "
        + "what came back; act on the whole list only when it is false.";

    private const string GetTodoSelectionDescription =
        "Re-read specific TODOs by identifier. Call this after a write reports a "
        + "conflict, or whenever the current state of a known set matters, "
        + "because it reports what those TODOs are now without disturbing what "
        + "the user is looking at.";

    private const string CreateTodoDescription =
        "Create one TODO. Call this when the user describes something new to do. "
        + "Ask for a due date rather than inventing one.";

    private static readonly string ChangeTodoStatusDescription =
        "Set the status of up to "
        + BulkTodoLimits.MaximumSelectionSize
        + " TODOs at once. Call this for \"mark these done\", \"start these\", "
        + "or \"archive these\". The batch applies in full or not at all, so "
        + "there is no partial outcome to report. Read the TODOs first: this "
        + "writes against the version they were last read at.";

    private static readonly string DeleteTodosDescription =
        "Propose deleting up to "
        + BulkTodoLimits.MaximumSelectionSize
        + " TODOs. Call this when the user asks to delete, remove, or get rid "
        + "of TODOs. This does not delete anything: it asks the user to confirm "
        + "and ends your turn. Say nothing further after calling it — they have "
        + "not answered yet. Deletion is recoverable from the trash for ninety "
        + "days.";

    private static readonly string RestoreTodosDescription =
        "Restore up to "
        + BulkTodoLimits.MaximumSelectionSize
        + " deleted TODOs from the trash. Call this for \"undo that\" or \"bring "
        + "those back\". Read them first with scope Deleted.";

    public static IReadOnlyList<AITool> Create(TodoTools tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return new AITool[]
        {
            Tool(tools.GetTodosAsync, TodoToolNames.GetTodos, GetTodosDescription),
            Tool(tools.GetTodoSelectionAsync, TodoToolNames.GetTodoSelection, GetTodoSelectionDescription),
            Tool(tools.CreateTodoAsync, TodoToolNames.CreateTodo, CreateTodoDescription),
            Tool(tools.ChangeTodoStatusAsync, TodoToolNames.ChangeTodoStatus, ChangeTodoStatusDescription),
            Tool(tools.DeleteTodosAsync, TodoToolNames.DeleteTodos, DeleteTodosDescription),
            Tool(tools.RestoreTodosAsync, TodoToolNames.RestoreTodos, RestoreTodosDescription),
        };
    }

    /// <summary>
    /// The parameter schema is read off the method itself — its parameter names
    /// and <c>Description</c> attributes — so only the name and the trigger
    /// condition are supplied here.
    /// </summary>
    private static AITool Tool(Delegate method, string name, string description)
    {
        return AIFunctionFactory.Create(
            method,
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
            });
    }
}
