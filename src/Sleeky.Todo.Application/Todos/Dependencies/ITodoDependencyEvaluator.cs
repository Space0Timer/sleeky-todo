namespace Sleeky.Todo.Application.Todos.Dependencies;

public interface ITodoDependencyEvaluator
{
    Task<TodoDependencyState> EvaluateAsync(
        IEnumerable<string> dependencyIds,
        CancellationToken cancellationToken = default);
}
