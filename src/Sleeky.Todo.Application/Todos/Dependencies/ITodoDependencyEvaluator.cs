namespace Sleeky.Todo.Application.Todos.Dependencies;

/// <summary>
/// Decides whether a TODO's prerequisites currently allow it to move forward.
/// </summary>
/// <remarks>
/// Whether a prerequisite is complete cannot be decided from one TODO, so the
/// entity records only the identifiers and this evaluates them on demand. The
/// answer is computed per request rather than stored, because it goes stale on
/// every completion, deletion, or archive of a prerequisite.
/// </remarks>
public interface ITodoDependencyEvaluator
{
    /// <summary>
    /// Counts the prerequisites among <paramref name="dependencyIds"/> that are
    /// not yet satisfied.
    /// </summary>
    /// <remarks>
    /// Only a completed, non-deleted prerequisite satisfies. One that is
    /// missing, deleted, archived, or otherwise not completed counts as
    /// incomplete — a dependent cannot proceed on the strength of a
    /// prerequisite that no longer exists. Duplicate identifiers count once.
    /// </remarks>
    Task<TodoDependencyState> EvaluateAsync(
        IEnumerable<Guid> dependencyIds,
        CancellationToken cancellationToken = default);
}
