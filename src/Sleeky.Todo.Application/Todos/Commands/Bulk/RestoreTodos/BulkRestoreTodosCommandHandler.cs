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
        // that has to look past the soft-delete filter to find its selection,
        // and the one whose writes must expect deleted documents.
        IReadOnlyList<TodoItem> todos = await BulkTodoBatch.LoadAsync(
            todoRepository,
            request.Items,
            cancellationToken,
            includeDeleted: true);

        // Restoration has no dependency gate: a restored TODO blocks nothing,
        // and its own prerequisites are evaluated when it next changes status.
        RestoreAll(todos);
        await BulkTodoBatch.SaveAsync(
            todoRepository,
            transactionExecutor,
            updates: todos,
            inserts: Array.Empty<TodoItem>(),
            cancellationToken,
            expectDeleted: true);

        this.logger.LogInformation(
            1112,
            "Bulk TODO restoration returned {TodoCount} TODOs to the active list",
            todos.Count);

        return BulkTodoResult.FromEntities(todos, written: todos);
    }

    /// <summary>
    /// One instant is read for the whole batch so every write it makes shares
    /// it.
    /// </summary>
    private void RestoreAll(IReadOnlyList<TodoItem> todos)
    {
        DateTimeOffset restoredAt = clock.UtcNow;
        foreach (TodoItem todoItem in todos)
        {
            todoItem.Restore(restoredAt);
        }
    }
}
