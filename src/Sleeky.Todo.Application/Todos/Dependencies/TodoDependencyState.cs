namespace Sleeky.Todo.Application.Todos.Dependencies;

/// <summary>
/// The outcome of evaluating a TODO's prerequisites: how many are not yet
/// satisfied, where missing, deleted, and not-completed each count as one. See
/// <see cref="ITodoDependencyEvaluator"/> for the rule.
/// </summary>
public sealed record TodoDependencyState(int IncompleteDependencyCount)
{
    /// <summary>
    /// <c>true</c> while any prerequisite is unsatisfied; a blocked TODO may
    /// not move to In Progress or Completed.
    /// </summary>
    public bool IsBlocked => IncompleteDependencyCount > 0;
}
