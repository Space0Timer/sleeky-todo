using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Events;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Events;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;

public sealed class ChangeTodoStatusCommandHandler
    : IRequestHandler<ChangeTodoStatusCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly IDomainEventDispatcher domainEventDispatcher;
    private readonly ITodoDependencyEvaluator dependencyEvaluator;
    private readonly ILogger<ChangeTodoStatusCommandHandler> logger;
    private readonly ITodoRepository todoRepository;
    private readonly ITransactionExecutor transactionExecutor;

    public ChangeTodoStatusCommandHandler(
        ITodoRepository todoRepository,
        ITodoDependencyEvaluator dependencyEvaluator,
        IClock clock,
        ITransactionExecutor transactionExecutor,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<ChangeTodoStatusCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(dependencyEvaluator);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(transactionExecutor);
        ArgumentNullException.ThrowIfNull(domainEventDispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.dependencyEvaluator = dependencyEvaluator;
        this.clock = clock;
        this.transactionExecutor = transactionExecutor;
        this.domainEventDispatcher = domainEventDispatcher;
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
        TodoCompletedDomainEvent? completionEvent = todoItem.DomainEvents
            .OfType<TodoCompletedDomainEvent>()
            .SingleOrDefault();
        TodoItem updatedTodo = await PersistStatusChangeAsync(
            todoItem,
            completionEvent,
            cancellationToken);

        this.logger.LogInformation(
            1108,
            "Changed TODO {TodoId} status from {PreviousStatus} to {Status} at version {Version}",
            updatedTodo.Id,
            previousStatus,
            updatedTodo.Status,
            updatedTodo.Version);

        if (completionEvent?.NextOccurrenceId is not null
            && completionEvent.SeriesId is not null)
        {
            this.logger.LogInformation(
                1101,
                "Created recurring TODO {TodoId} for series {SeriesId} after completing TODO {CompletedTodoId}",
                completionEvent.NextOccurrenceId,
                completionEvent.SeriesId,
                completionEvent.TodoId);
        }

        todoItem.ClearDomainEvents();

        return TodoDto.FromEntity(updatedTodo, completionEvent?.NextOccurrenceId);
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
    /// A completion may also insert the next recurring occurrence, so those two
    /// writes share a transaction. Every other status change is a single write.
    /// </summary>
    private Task<TodoItem> PersistStatusChangeAsync(
        TodoItem todoItem,
        TodoCompletedDomainEvent? completionEvent,
        CancellationToken cancellationToken)
    {
        if (completionEvent is null)
        {
            return todoRepository.UpdateAsync(todoItem, cancellationToken);
        }

        return transactionExecutor.ExecuteAsync(
            async transactionCancellationToken =>
            {
                TodoItem persistedTodo = await todoRepository.UpdateAsync(
                    todoItem,
                    transactionCancellationToken);
                await domainEventDispatcher.DispatchAsync(
                    todoItem.DomainEvents,
                    transactionCancellationToken);
                return persistedTodo;
            },
            cancellationToken);
    }
}
