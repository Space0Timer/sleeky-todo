using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Todos.Dependencies;

public sealed class DependencyCycleDetector : IDependencyCycleDetector
{
    /// <summary>
    /// Bounds on how far a single cycle check will walk.
    /// </summary>
    /// <remarks>
    /// The traversal always terminates — <c>visited</c> makes sure of that —
    /// but nothing bounds how much it reads on the way. A pathological graph
    /// could hold the request open while it pages through the Space's whole
    /// set, so the walk gives up and reports a conflict instead. Both limits
    /// sit far above any hand-built dependency chain; reaching either means the
    /// graph is not one a person authored.
    /// </remarks>
    private const int MaxTraversalDepth = 64;
    private const int MaxTraversalNodes = 10_000;

    private readonly ITodoRepository todoRepository;

    public DependencyCycleDetector(ITodoRepository todoRepository)
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

            Guid[] batchIds = ClaimUnvisited(frontier, visited);
            if (batchIds.Length == 0)
            {
                return false;
            }

            // Only the edges matter here, so the walk reads a projection rather
            // than materialising an aggregate per node.
            IReadOnlyCollection<TodoDependencyNode> batch =
                await todoRepository.GetDependencyNodesAsync(
                    batchIds,
                    cancellationToken: cancellationToken);

            frontier = BuildNextFrontier(batch, visited);
        }

        return false;
    }

    /// <summary>
    /// Returns the frontier's not-yet-visited nodes and marks them visited, so
    /// no node is fetched twice however many paths lead to it.
    /// </summary>
    /// <remarks>
    /// The filter only reads; marking is a separate pass. Folding the two
    /// together works solely because the sequence is enumerated once and
    /// immediately, which is not a property the next edit here has to
    /// preserve.
    /// </remarks>
    private static Guid[] ClaimUnvisited(
        IReadOnlySet<Guid> frontier,
        HashSet<Guid> visited)
    {
        Guid[] batchIds = frontier
            .Where(dependencyId => !visited.Contains(dependencyId))
            .ToArray();

        foreach (Guid dependencyId in batchIds)
        {
            visited.Add(dependencyId);
        }

        return batchIds;
    }

    /// <summary>
    /// Collects the next level, refusing the walk as soon as it would carry more
    /// than <see cref="MaxTraversalNodes"/> nodes.
    /// </summary>
    /// <remarks>
    /// The budget is enforced while the level is being accumulated rather than
    /// on the following pass, so the set that breaches it is never finished. A
    /// single level of large fan-out would otherwise be materialised in full and
    /// only rejected afterwards, which spends exactly the memory the cap is
    /// there to refuse.
    /// </remarks>
    private static HashSet<Guid> BuildNextFrontier(
        IReadOnlyCollection<TodoDependencyNode> batch,
        HashSet<Guid> visited)
    {
        HashSet<Guid> frontier = new HashSet<Guid>();

        foreach (TodoDependencyNode node in batch)
        {
            foreach (Guid dependencyId in node.DependencyIds)
            {
                if (visited.Contains(dependencyId) || !frontier.Add(dependencyId))
                {
                    continue;
                }

                if (visited.Count + frontier.Count > MaxTraversalNodes)
                {
                    throw new DomainException(
                        "The dependency graph is too large to validate.");
                }
            }
        }

        return frontier;
    }
}
