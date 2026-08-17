using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Assistant.Conflicts;

/// <summary>
/// The assistant's bulk dispatch site. Every batch the assistant writes goes
/// through here, so the retry rule is stated once and the origin marker is
/// applied once. Each batch names the Space it acts in, which the turn fixed
/// before any tool ran; the model never chooses it.
/// </summary>
public interface IBulkConflictPolicy
{
    Task<BulkTodoResult> ChangeStatusAsync(
        Guid spaceId,
        TodoStatus status,
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken);

    Task<BulkTodoResult> DeleteAsync(
        Guid spaceId,
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken);

    Task<BulkTodoResult> RestoreAsync(
        Guid spaceId,
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken);
}
