namespace Sleeky.Todo.Application.Todos.Dependencies;

public interface ITodoDependencyEvaluator
{
    Task<TodoDependencyState> EvaluateAsync(
        IEnumerable<Guid> dependencyIds,
        CancellationToken cancellationToken = default);
}

public sealed class TodoDependencyState
{
    public TodoDependencyState(int incompleteDependencyCount)
    {
        IncompleteDependencyCount = incompleteDependencyCount;
    }

    public bool IsBlocked => IncompleteDependencyCount > 0;

    public int IncompleteDependencyCount { get; }
}
