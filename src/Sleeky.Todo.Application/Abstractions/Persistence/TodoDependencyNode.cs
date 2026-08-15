using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Abstractions.Persistence;

/// <summary>
/// The slice of a TODO that dependency reasoning needs: what it points at, and
/// whether it counts as satisfied.
/// </summary>
/// <remarks>
/// Cycle detection walks the graph and blocked evaluation scores a node's
/// prerequisites; neither reads a name, a description, or a recurrence. Loading
/// whole <see cref="Domain.Entities.TodoItem"/> aggregates to answer those
/// questions makes the traversal cost scale with document size for no benefit,
/// so the repository projects to this instead.
/// </remarks>
public sealed record TodoDependencyNode(
    Guid Id,
    TodoStatus Status,
    bool IsDeleted,
    IReadOnlyCollection<Guid> DependencyIds);
