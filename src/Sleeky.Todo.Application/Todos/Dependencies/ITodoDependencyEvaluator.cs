namespace Sleeky.Todo.Application.Todos.Dependencies;

public interface ITodoDependencyEvaluator
{
    Task<TodoDependencyState> EvaluateAsync(
        IEnumerable<Guid> dependencyIds,
        CancellationToken cancellationToken = default);
}
