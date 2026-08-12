using MediatR;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;

public sealed class ChangeTodoStatusCommandHandler
    : IRequestHandler<ChangeTodoStatusCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ITodoDependencyEvaluator dependencyEvaluator;
    private readonly ITodoRepository todoRepository;

    public ChangeTodoStatusCommandHandler(
        ITodoRepository todoRepository,
        ITodoDependencyEvaluator dependencyEvaluator,
        IClock clock)
    {
        this.todoRepository = todoRepository;
        this.dependencyEvaluator = dependencyEvaluator;
        this.clock = clock;
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
        TodoItem updatedTodo = await todoRepository.UpdateAsync(
            todoItem,
            request.Version,
            cancellationToken)
            ?? throw new ConcurrencyConflictException("TODO", request.Id, request.Version);

        return TodoDto.FromEntity(updatedTodo);
    }
}
