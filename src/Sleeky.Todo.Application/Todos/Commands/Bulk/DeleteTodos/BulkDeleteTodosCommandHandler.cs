using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.DeleteTodos;

public sealed class BulkDeleteTodosCommandHandler
    : IRequestHandler<BulkDeleteTodosCommand, BulkTodoResult>
{
    private readonly IClock clock;
    private readonly ILogger<BulkDeleteTodosCommandHandler> logger;
    private readonly ITodoRepository todoRepository;
    private readonly ITransactionExecutor transactionExecutor;

    public BulkDeleteTodosCommandHandler(
        ITodoRepository todoRepository,
        IClock clock,
        ITransactionExecutor transactionExecutor,
        ILogger<BulkDeleteTodosCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(transactionExecutor);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.clock = clock;
        this.transactionExecutor = transactionExecutor;
        this.logger = logger;
    }

    public async Task<BulkTodoResult> Handle(
        BulkDeleteTodosCommand request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TodoItem> todos = await BulkTodoLoader.LoadAsync(
            todoRepository,
            request.Items,
            cancellationToken);
        await EnsureNoDependentsLeftBehindAsync(todos, cancellationToken);

        SoftDeleteAll(todos);
        await PersistAsync(todos, cancellationToken);

        this.logger.LogInformation(
            1111,
            "Bulk TODO deletion removed {TodoCount} TODOs; scheduled purge at {PurgeAt}",
            todos.Count,
            todos[0].PurgeAt);

        return BuildResult(todos);
    }

    /// <summary>
    /// Every item was written, so each reports the version the write produced,
    /// which the entity itself never advances.
    /// </summary>
    private static BulkTodoResult BuildResult(IReadOnlyList<TodoItem> todos)
    {
        return new BulkTodoResult(todos
            .Select(todoItem => new BulkTodoResultItem(
                todoItem.Id,
                todoItem.Version + 1,
                todoItem.Status,
                todoItem.DeletedAt,
                null))
            .ToArray());
    }

    /// <summary>
    /// Dependents inside the batch are being deleted too, so only dependents
    /// left behind can block the deletion.
    /// </summary>
    private async Task EnsureNoDependentsLeftBehindAsync(
        IReadOnlyList<TodoItem> todos,
        CancellationToken cancellationToken)
    {
        Guid[] selectedIds = todos.Select(todoItem => todoItem.Id).ToArray();
        IReadOnlyCollection<Guid> blockingDependentIds = await todoRepository
            .GetActiveDependentIdsAsync(selectedIds, selectedIds, cancellationToken);

        if (blockingDependentIds.Count > 0)
        {
            throw new DomainException(
                "A TODO with active dependents cannot be deleted.");
        }
    }

    /// <summary>
    /// One instant is read for the whole batch so every write it makes shares
    /// it.
    /// </summary>
    private void SoftDeleteAll(IReadOnlyList<TodoItem> todos)
    {
        DateTimeOffset deletedAt = clock.UtcNow;
        foreach (TodoItem todoItem in todos)
        {
            todoItem.SoftDelete(deletedAt);
        }
    }

    /// <summary>
    /// A batch that writes a single document does not need a transaction, which
    /// keeps bulk requests working against a standalone MongoDB deployment.
    /// </summary>
    private Task PersistAsync(
        IReadOnlyCollection<TodoItem> updates,
        CancellationToken cancellationToken)
    {
        if (updates.Count == 1)
        {
            return todoRepository.SaveBatchAsync(
                updates,
                Array.Empty<TodoItem>(),
                cancellationToken);
        }

        return transactionExecutor.ExecuteAsync(
            async transactionCancellationToken =>
            {
                await todoRepository.SaveBatchAsync(
                    updates,
                    Array.Empty<TodoItem>(),
                    transactionCancellationToken);
                return true;
            },
            cancellationToken);
    }
}
