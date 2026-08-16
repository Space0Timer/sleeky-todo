using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

/// <summary>
/// Which of the three mutually exclusive lists a query reads. Every TODO is in
/// exactly one: deletion takes precedence, then archived status.
/// </summary>
/// <remarks>
/// A scope, not a status filter: <see cref="TodoStatus"/> can still be
/// filtered within <see cref="Active"/>, and archived TODOs are excluded from
/// it even though Archived is a status, so the main list never shows what has
/// been put away.
/// </remarks>
public enum TodoListScope
{
    /// <summary>Not deleted and not archived — the working list.</summary>
    Active = 0,

    /// <summary>Not deleted, status Archived.</summary>
    Archived = 1,

    /// <summary>
    /// Soft-deleted — Trash. Retention is enforced on restore, not here, so a
    /// TODO whose window has passed still lists until it is purged.
    /// </summary>
    Deleted = 2,
}
