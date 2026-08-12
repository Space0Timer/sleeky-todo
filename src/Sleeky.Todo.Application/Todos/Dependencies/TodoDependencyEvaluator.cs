using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Entities;
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

        IReadOnlyCollection<TodoItem> dependencies = await todoRepository.GetByIdsAsync(
            distinctIds,
            includeDeleted: true,
            cancellationToken);
        int missingCount = distinctIds.Length - dependencies.Count;
        int incompleteCount = dependencies.Count(dependency =>
            dependency.DeletedAt is not null
            || dependency.Status != TodoStatus.Completed);

        return new TodoDependencyState(missingCount + incompleteCount);
    }
}
