using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Commands.Bulk.RestoreTodos;

public sealed class BulkRestoreTodosCommandHandler
    : IRequestHandler<BulkRestoreTodosCommand, BulkTodoResult>
{
    private readonly IClock clock;
    private readonly ILogger<BulkRestoreTodosCommandHandler> logger;
    private readonly ITodoRepository todoRepository;
    private readonly ITransactionExecutor transactionExecutor;

    public BulkRestoreTodosCommandHandler(
        ITodoRepository todoRepository,
        IClock clock,
        ITransactionExecutor transactionExecutor,
        ILogger<BulkRestoreTodosCommandHandler> logger)
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
        BulkRestoreTodosCommand request,
        CancellationToken cancellationToken)
    {
        // Every selected TODO is deleted by definition, so this is the one batch
        // that has to look past the soft-delete filter to find its selection.
        IReadOnlyList<TodoItem> todos = await BulkTodoLoader.LoadAsync(
            todoRepository,
            request.Items,
            cancellationToken,
            includeDeleted: true);

        // Restoration has no dependency gate: a restored TODO blocks nothing,
        // and its own prerequisites are evaluated when it next changes status.
        DateTimeOffset restoredAt = clock.UtcNow;
        foreach (TodoItem todoItem in todos)
        {
            todoItem.Restore(restoredAt);
        }

        await PersistAsync(todos, cancellationToken);

        this.logger.LogInformation(
            1112,
            "Bulk TODO restoration returned {TodoCount} TODOs to the active list",
            todos.Count);

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
                cancellationToken,
                expectDeleted: true);
        }

        return transactionExecutor.ExecuteAsync(
            async transactionCancellationToken =>
            {
                await todoRepository.SaveBatchAsync(
                    updates,
                    Array.Empty<TodoItem>(),
                    transactionCancellationToken,
                    expectDeleted: true);
                return true;
            },
            cancellationToken);
    }
}
