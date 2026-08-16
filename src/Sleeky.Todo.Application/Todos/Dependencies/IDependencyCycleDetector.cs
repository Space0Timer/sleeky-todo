using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Todos.Dependencies;

/// <summary>
/// Answers whether adding one dependency edge would close a cycle in the
/// owner's dependency graph.
/// </summary>
/// <remarks>
/// A cycle would leave every TODO on it permanently blocked, since each waits
/// on another that waits on it, so the edge is refused before it is written.
/// The check needs the rest of the graph, which is why it lives in the
/// Application layer rather than on the entity.
/// </remarks>
public interface IDependencyCycleDetector
{
    /// <summary>
    /// Returns <c>true</c> when making <paramref name="sourceTodoId"/> depend on
    /// <paramref name="dependencyTodoId"/> would create a cycle — that is, when
    /// the source is already reachable by following dependencies outward from
    /// the proposed dependency.
    /// </summary>
    /// <remarks>
    /// The proposed edge is not yet in the graph when this runs; the caller
    /// adds it only on a <c>false</c> answer. A self-dependency is a cycle by
    /// this definition, but callers refuse it first with a clearer message.
    /// </remarks>
    /// <exception cref="DomainException">
    /// The walk exceeded its depth or node budget before finding an answer. The
    /// limits sit far above any hand-built chain, so reaching one is reported
    /// as a conflict rather than answered optimistically.
    /// </exception>
    Task<bool> WouldCreateCycleAsync(
        Guid sourceTodoId,
        Guid dependencyTodoId,
        CancellationToken cancellationToken = default);
}
