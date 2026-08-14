using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Assistant.Conflicts;

/// <summary>
/// The assistant's bulk dispatch site. Every batch the assistant writes goes
/// through here, so the retry rule is stated once and the origin marker is
/// applied once.
/// </summary>
public interface IBulkConflictPolicy
{
    Task<BulkTodoResult> ChangeStatusAsync(
        TodoStatus status,
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken);

    Task<BulkTodoResult> DeleteAsync(
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken);

    Task<BulkTodoResult> RestoreAsync(
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken);
}
