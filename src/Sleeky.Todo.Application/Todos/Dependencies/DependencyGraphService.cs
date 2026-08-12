using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Dependencies;

public sealed class DependencyGraphService : IDependencyGraphService
{
    private readonly ITodoRepository todoRepository;

    public DependencyGraphService(ITodoRepository todoRepository)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);

        this.todoRepository = todoRepository;
    }

    public async Task<bool> WouldCreateCycleAsync(
        Guid sourceTodoId,
        Guid dependencyTodoId,
        CancellationToken cancellationToken = default)
    {
        HashSet<Guid> visited = new HashSet<Guid>();
        HashSet<Guid> frontier = new HashSet<Guid>([dependencyTodoId]);

        while (frontier.Count > 0)
        {
            if (frontier.Contains(sourceTodoId))
            {
                return true;
            }

            Guid[] batchIds = frontier
                .Where(visited.Add)
                .ToArray();
            if (batchIds.Length == 0)
            {
                return false;
            }

            IReadOnlyCollection<TodoItem> batch = await todoRepository.GetByIdsAsync(
                batchIds,
                cancellationToken: cancellationToken);
            frontier = batch
                .SelectMany(todo => todo.DependencyIds)
                .Where(dependencyId => !visited.Contains(dependencyId))
                .ToHashSet();
        }

        return false;
    }
}
