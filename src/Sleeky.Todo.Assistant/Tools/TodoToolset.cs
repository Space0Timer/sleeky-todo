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
    public static IReadOnlyList<AITool> Create(TodoTools tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return new AITool[]
        {
            AIFunctionFactory.Create(
                tools.GetTodosAsync,
                new AIFunctionFactoryOptions
                {
                    Name = TodoToolNames.GetTodos,
                    Description =
                        "Read the user's TODOs. Call this whenever a request names TODOs by "
                        + "description rather than by identifier — \"everything due this week\", "
                        + "\"the high-priority ones\", \"what's in the trash\" — because the "
                        + "answer carries the identifiers and versions every other tool needs. "
                        + "When the request names them by what they are about — \"the ones about "
                        + "the tax return\" — pass those words as search rather than reading the "
                        + "whole list and picking. Returns hasMore when the list is longer than "
                        + "what came back; act on the whole list only when it is false.",
                }),
            AIFunctionFactory.Create(
                tools.GetTodoSelectionAsync,
                new AIFunctionFactoryOptions
                {
                    Name = TodoToolNames.GetTodoSelection,
                    Description =
                        "Re-read specific TODOs by identifier. Call this after a write reports a "
                        + "conflict, or whenever the current state of a known set matters, "
                        + "because it reports what those TODOs are now without disturbing what "
                        + "the user is looking at.",
                }),
            AIFunctionFactory.Create(
                tools.CreateTodoAsync,
                new AIFunctionFactoryOptions
                {
                    Name = TodoToolNames.CreateTodo,
                    Description =
                        "Create one TODO. Call this when the user describes something new to do. "
                        + "Ask for a due date rather than inventing one.",
                }),
            AIFunctionFactory.Create(
                tools.ChangeTodoStatusAsync,
                new AIFunctionFactoryOptions
                {
                    Name = TodoToolNames.ChangeTodoStatus,
                    Description =
                        "Set the status of up to "
                        + BulkTodoLimits.MaximumSelectionSize
                        + " TODOs at once. Call this for \"mark these done\", \"start these\", "
                        + "or \"archive these\". The batch applies in full or not at all, so "
                        + "there is no partial outcome to report. Read the TODOs first: this "
                        + "writes against the version they were last read at.",
                }),
            AIFunctionFactory.Create(
                tools.DeleteTodosAsync,
                new AIFunctionFactoryOptions
                {
                    Name = TodoToolNames.DeleteTodos,
                    Description =
                        "Propose deleting up to "
                        + BulkTodoLimits.MaximumSelectionSize
                        + " TODOs. Call this when the user asks to delete, remove, or get rid "
                        + "of TODOs. This does not delete anything: it asks the user to confirm "
                        + "and ends your turn. Say nothing further after calling it — they have "
                        + "not answered yet. Deletion is recoverable from the trash for ninety "
                        + "days.",
                }),
            AIFunctionFactory.Create(
                tools.RestoreTodosAsync,
                new AIFunctionFactoryOptions
                {
                    Name = TodoToolNames.RestoreTodos,
                    Description =
                        "Restore up to "
                        + BulkTodoLimits.MaximumSelectionSize
                        + " deleted TODOs from the trash. Call this for \"undo that\" or \"bring "
                        + "those back\". Read them first with scope Deleted.",
                }),
        };
    }
}
