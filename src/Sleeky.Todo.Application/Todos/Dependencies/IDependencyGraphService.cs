namespace Sleeky.Todo.Application.Todos.Dependencies;

public interface IDependencyGraphService
{
    Task<bool> WouldCreateCycleAsync(
        Guid sourceTodoId,
        Guid dependencyTodoId,
        CancellationToken cancellationToken = default);
}
