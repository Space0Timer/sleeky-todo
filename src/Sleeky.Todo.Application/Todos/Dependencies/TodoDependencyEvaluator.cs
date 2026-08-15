using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Dependencies;

public sealed class TodoDependencyEvaluator : ITodoDependencyEvaluator
{
    private readonly ITodoRepository todoRepository;

    public TodoDependencyEvaluator(ITodoRepository todoRepository)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);

        this.todoRepository = todoRepository;
    }

    public async Task<TodoDependencyState> EvaluateAsync(
        IEnumerable<Guid> dependencyIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencyIds);

        Guid[] distinctIds = dependencyIds
            .Distinct()
            .ToArray();
        if (distinctIds.Length == 0)
        {
            return new TodoDependencyState(0);
        }

        // Only the status of each prerequisite matters, so this reads a
        // projection rather than materialising an aggregate per dependency.
        IReadOnlyCollection<TodoDependencyNode> dependencies =
            await todoRepository.GetDependencyNodesAsync(
                distinctIds,
                includeDeleted: true,
                cancellationToken);
        int missingCount = distinctIds.Length - dependencies.Count;
        int incompleteCount = dependencies.Count(dependency =>
            dependency.IsDeleted
            || dependency.Status != TodoStatus.Completed);

        return new TodoDependencyState(missingCount + incompleteCount);
    }
}
