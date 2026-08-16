using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Application.Todos.Recurrence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;

public sealed class ChangeTodoStatusCommandHandler
    : IRequestHandler<ChangeTodoStatusCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ITodoDependencyEvaluator dependencyEvaluator;
    private readonly ILogger<ChangeTodoStatusCommandHandler> logger;
    private readonly IRecurringOccurrenceFactory recurringOccurrenceFactory;
    private readonly ITodoRepository todoRepository;
    private readonly ITransactionExecutor transactionExecutor;

    public ChangeTodoStatusCommandHandler(
        ITodoRepository todoRepository,
        ITodoDependencyEvaluator dependencyEvaluator,
        IClock clock,
        ITransactionExecutor transactionExecutor,
        IRecurringOccurrenceFactory recurringOccurrenceFactory,
        ILogger<ChangeTodoStatusCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(dependencyEvaluator);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(transactionExecutor);
        ArgumentNullException.ThrowIfNull(recurringOccurrenceFactory);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.dependencyEvaluator = dependencyEvaluator;
        this.clock = clock;
        this.transactionExecutor = transactionExecutor;
        this.recurringOccurrenceFactory = recurringOccurrenceFactory;
        this.logger = logger;
    }

    public async Task<TodoDto> Handle(
        ChangeTodoStatusCommand request,
        CancellationToken cancellationToken)
    {
        TodoItem todoItem = await todoRepository.GetByIdAsync(
            request.Id,
            cancellationToken: cancellationToken)
            ?? throw new NotFoundException("TODO", request.Id);
        TodoVersionGuard.EnsureExpectedVersion(todoItem, request.Version);

        if (todoItem.Status == request.Status)
        {
            return TodoDto.FromEntity(todoItem);
        }

        await EnsureDependenciesAllowTransitionAsync(
            todoItem,
            request.Status,
            cancellationToken);

        TodoStatus previousStatus = todoItem.Status;
        _ = todoItem.ChangeStatus(request.Status, clock.UtcNow);
        TodoCompletion? completion = todoItem.Completion;
        TodoItem updatedTodo = await PersistStatusChangeAsync(
            todoItem,
            completion,
            cancellationToken);

        this.logger.LogInformation(
            1108,
            "Changed TODO {TodoId} status from {PreviousStatus} to {Status} at version {Version}",
            updatedTodo.Id,
            previousStatus,
            updatedTodo.Status,
            updatedTodo.Version);

        if (completion?.NextOccurrenceId is not null
            && completion.SeriesId is not null)
        {
            this.logger.LogInformation(
                1101,
                "Created recurring TODO {TodoId} for series {SeriesId} after completing TODO {CompletedTodoId}",
                completion.NextOccurrenceId,
                completion.SeriesId,
                completion.TodoId);
        }

        return TodoDto.FromEntity(updatedTodo, completion?.NextOccurrenceId);
    }

    private async Task EnsureDependenciesAllowTransitionAsync(
        TodoItem todoItem,
        TodoStatus status,
        CancellationToken cancellationToken)
    {
        if (status != TodoStatus.InProgress && status != TodoStatus.Completed)
        {
            return;
        }

        TodoDependencyState dependencyState = await dependencyEvaluator.EvaluateAsync(
            todoItem.DependencyIds,
            cancellationToken);
        if (!dependencyState.IsBlocked)
        {
            return;
        }

        throw new DomainException($"A blocked TODO cannot move to {status}.");
    }

    /// <summary>
    /// A recurring completion also inserts the next occurrence, so those two
    /// writes share a transaction. Every other status change, including
    /// completing a non-recurring TODO, is a single write.
    /// </summary>
    /// <remarks>
    /// The successor is built and inserted here rather than dispatched to a
    /// separate handler, matching what the bulk path does with the same factory.
    /// Both writes are then plainly inside the transaction, and an insert
    /// failure aborts the completion.
    /// </remarks>
    private Task<TodoItem> PersistStatusChangeAsync(
        TodoItem todoItem,
        TodoCompletion? completion,
        CancellationToken cancellationToken)
    {
        if (completion?.Recurrence is null)
        {
            return todoRepository.UpdateAsync(todoItem, cancellationToken);
        }

        return transactionExecutor.ExecuteAsync(
            async transactionCancellationToken =>
            {
                TodoItem persistedTodo = await todoRepository.UpdateAsync(
                    todoItem,
                    transactionCancellationToken);
                await todoRepository.AddAsync(
                    recurringOccurrenceFactory.CreateNext(completion),
                    transactionCancellationToken);

                return persistedTodo;
            },
            cancellationToken);
    }
}
