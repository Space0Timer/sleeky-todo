using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;

/// <summary>
/// The TODOs that were found, in request order. Identifiers that no longer
/// resolve are absent rather than reported: this is a probe used to discover
/// what a selection still refers to, unlike a write, which refuses the whole
/// batch when an identifier is missing. Soft-deleted TODOs still resolve, since
/// the trash lists them and a selection there is restorable.
/// </summary>
public sealed record TodoSelection(IReadOnlyCollection<TodoDto> Items);
