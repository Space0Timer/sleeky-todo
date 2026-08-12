namespace Sleeky.Todo.Application.Todos.Dependencies;

public interface IDependencyGraphService
{
    Task<bool> WouldCreateCycleAsync(
        string sourceTodoId,
        string dependencyTodoId,
        CancellationToken cancellationToken = default);
}
