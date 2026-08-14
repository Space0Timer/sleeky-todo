using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Commands.Bulk.ChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Commands.Bulk.DeleteTodos;
using Sleeky.Todo.Application.Todos.Commands.Bulk.RestoreTodos;
using Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Assistant.Conflicts;

/// <summary>
/// Decides what happens to a batch that loses a version race.
/// </summary>
/// <remarks>
/// This mirrors the browser's policy in <c>useBulkActions.ts</c>; see "Retrying
/// a conflicted batch without asking" in <c>docs/decision-log.md</c> for why
/// both copies exist and which invariants each holds independently.
///
/// The policy lives here rather than in the handlers because those are shared
/// with the HTTP path: a retry inside one would make the browser's writes
/// silently retry too.
/// </remarks>
public sealed class BulkConflictPolicy : IBulkConflictPolicy
{
    private readonly ISender sender;

    private readonly ILogger<BulkConflictPolicy> logger;

    public BulkConflictPolicy(ISender sender, ILogger<BulkConflictPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(logger);

        this.sender = sender;
        this.logger = logger;
    }

    /// <summary>
    /// Retried once, and only here. A status change is idempotent, an
    /// already-satisfied item is a no-op that echoes its version unchanged, and
    /// the domain guards reject the transitions that would be wrong, so a retry
    /// either converges on the user's intent or fails loudly with the real
    /// reason.
    /// </summary>
    public async Task<BulkTodoResult> ChangeStatusAsync(
        TodoStatus status,
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        try
        {
            return await this.DispatchAsync(
                new BulkChangeTodoStatusCommand(status, items),
                cancellationToken);
        }
        catch (BulkConcurrencyConflictException)
        {
            BulkTodoResult? retried = await this.RetryStatusAsync(status, items, cancellationToken);

            if (retried is null)
            {
                throw;
            }

            return retried;
        }
        catch (ConcurrencyConflictException)
        {
            BulkTodoResult? retried = await this.RetryStatusAsync(status, items, cancellationToken);

            if (retried is null)
            {
                throw;
            }

            return retried;
        }
    }

    /// <summary>
    /// Never retried. Deletion is the batch whose intent can invert while the
    /// world moves — a TODO archived as junk may have been reopened elsewhere —
    /// so a conflict goes back to a person.
    /// </summary>
    public Task<BulkTodoResult> DeleteAsync(
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken)
    {
        return this.DispatchAsync(new BulkDeleteTodosCommand(items), cancellationToken);
    }

    /// <summary>
    /// Never retried. A conflicted restore means someone has already restored
    /// it, and the write asserts the stored document is still deleted, so a
    /// second attempt would fail anyway.
    /// </summary>
    public Task<BulkTodoResult> RestoreAsync(
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken)
    {
        return this.DispatchAsync(new BulkRestoreTodosCommand(items), cancellationToken);
    }

    /// <summary>
    /// Re-reads the selection and retries with what it found, or gives up.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the retry must not run, which the caller
    /// turns back into the original conflict.
    /// </returns>
    private async Task<BulkTodoResult?> RetryStatusAsync(
        TodoStatus status,
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<BulkTodoItemRequest>? refreshed =
            await this.RereadAsync(items, cancellationToken);

        if (refreshed is null)
        {
            return null;
        }

        return await this.DispatchAsync(
            new BulkChangeTodoStatusCommand(status, refreshed),
            cancellationToken);
    }

    /// <summary>
    /// Reads the selection's current versions. Any identifier that no longer
    /// resolves abandons the retry: acting on the remainder would write a
    /// subset nobody chose.
    /// </summary>
    private async Task<IReadOnlyCollection<BulkTodoItemRequest>?> RereadAsync(
        IReadOnlyCollection<BulkTodoItemRequest> items,
        CancellationToken cancellationToken)
    {
        Guid[] ids = items.Select(item => item.Id).ToArray();
        TodoSelection selection = await this.sender.Send(
            new GetTodoSelectionQuery(ids),
            cancellationToken);
        Dictionary<Guid, long> versions = selection.Items
            .ToDictionary(todo => todo.Id, todo => todo.Version);
        List<BulkTodoItemRequest> refreshed = new List<BulkTodoItemRequest>(ids.Length);

        foreach (Guid id in ids)
        {
            if (!versions.TryGetValue(id, out long version))
            {
                return null;
            }

            refreshed.Add(new BulkTodoItemRequest(id, version));
        }

        return refreshed;
    }

    private async Task<BulkTodoResult> DispatchAsync(
        IRequest<BulkTodoResult> command,
        CancellationToken cancellationToken)
    {
        using IDisposable? origin = AssistantOrigin.Begin(this.logger);

        return await this.sender.Send(command, cancellationToken);
    }
}
