using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Todos.Dependencies;

public sealed class DependencyGraphService : IDependencyGraphService
{
    /// <summary>
    /// Bounds on how far a single cycle check will walk.
    /// </summary>
    /// <remarks>
    /// The traversal always terminates — <c>visited</c> makes sure of that —
    /// but nothing bounds how much it reads on the way. A pathological graph
    /// could hold the request open while it pages through the owner's whole
    /// set, so the walk gives up and reports a conflict instead. Both limits
    /// sit far above any hand-built dependency chain; reaching either means the
    /// graph is not one a person authored.
    /// </remarks>
    private const int MaxTraversalDepth = 64;
    private const int MaxTraversalNodes = 10_000;

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

        for (int depth = 0; frontier.Count > 0; depth++)
        {
            if (frontier.Contains(sourceTodoId))
            {
                return true;
            }

            if (depth >= MaxTraversalDepth)
            {
                throw new DomainException(
                    "The dependency graph is too deep to validate.");
            }

            Guid[] batchIds = frontier
                .Where(visited.Add)
                .ToArray();
            if (batchIds.Length == 0)
            {
                return false;
            }

            if (visited.Count > MaxTraversalNodes)
            {
                throw new DomainException(
                    "The dependency graph is too large to validate.");
            }

            // Only the edges matter here, so the walk reads a projection rather
            // than materialising an aggregate per node.
            IReadOnlyCollection<TodoDependencyNode> batch =
                await todoRepository.GetDependencyNodesAsync(
                    batchIds,
                    cancellationToken: cancellationToken);
            frontier = batch
                .SelectMany(node => node.DependencyIds)
                .Where(dependencyId => !visited.Contains(dependencyId))
                .ToHashSet();
        }

        return false;
    }
}
