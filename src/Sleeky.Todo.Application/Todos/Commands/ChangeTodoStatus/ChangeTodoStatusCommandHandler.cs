using MediatR;

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
    private readonly ITodoRepository todoRepository;
    private readonly ITodoTransaction todoTransaction;

    public ChangeTodoStatusCommandHandler(
        ITodoRepository todoRepository,
        ITodoDependencyEvaluator dependencyEvaluator,
        IClock clock,
        ITodoTransaction todoTransaction,
        IDomainEventDispatcher domainEventDispatcher)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(dependencyEvaluator);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(todoTransaction);
        ArgumentNullException.ThrowIfNull(domainEventDispatcher);

        this.todoRepository = todoRepository;
        this.dependencyEvaluator = dependencyEvaluator;
        this.clock = clock;
        this.todoTransaction = todoTransaction;
        this.domainEventDispatcher = domainEventDispatcher;
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

        if (request.Status is TodoStatus.InProgress or TodoStatus.Completed)
        {
            TodoDependencyState dependencyState = await dependencyEvaluator.EvaluateAsync(
                todoItem.DependencyIds,
                cancellationToken);
            if (dependencyState.IsBlocked)
            {
                throw new DomainException(
                    $"A blocked TODO cannot move to {request.Status}.");
            }
        }

        _ = todoItem.ChangeStatus(request.Status, clock.UtcNow);
        TodoCompletedDomainEvent? completionEvent = todoItem.DomainEvents
            .OfType<TodoCompletedDomainEvent>()
            .SingleOrDefault();
        TodoItem updatedTodo;
        if (completionEvent is null)
        {
            updatedTodo = await PersistAsync(todoItem, request, cancellationToken);
        }
        else
        {
            updatedTodo = await todoTransaction.ExecuteAsync(
                request.Id,
                request.Version,
                async transactionCancellationToken =>
                {
                    TodoItem persistedTodo = await PersistAsync(
                        todoItem,
                        request,
                        transactionCancellationToken);
                    await domainEventDispatcher.DispatchAsync(
                        todoItem.DomainEvents,
                        transactionCancellationToken);
                    return persistedTodo;
                },
                cancellationToken);
        }

        todoItem.ClearDomainEvents();

        return TodoDto.FromEntity(updatedTodo, completionEvent?.NextOccurrenceId);
    }

    private async Task<TodoItem> PersistAsync(
        TodoItem todoItem,
        ChangeTodoStatusCommand request,
        CancellationToken cancellationToken)
    {
        return await todoRepository.UpdateAsync(
            todoItem,
            request.Version,
            cancellationToken)
            ?? throw new ConcurrencyConflictException("TODO", request.Id, request.Version);
    }
}
