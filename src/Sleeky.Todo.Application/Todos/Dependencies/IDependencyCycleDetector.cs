namespace Sleeky.Todo.Application.Todos.Dependencies;

public interface IDependencyCycleDetector
{
    Task<bool> WouldCreateCycleAsync(
        Guid sourceTodoId,
        Guid dependencyTodoId,
        CancellationToken cancellationToken = default);
}
