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
        string sourceTodoId,
        string dependencyTodoId,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> frontier = new HashSet<string>(
            [dependencyTodoId],
            StringComparer.Ordinal);

        while (frontier.Count > 0)
        {
            if (frontier.Contains(sourceTodoId))
            {
                return true;
            }

            string[] batchIds = frontier
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
                .ToHashSet(StringComparer.Ordinal);
        }

        return false;
    }
}
