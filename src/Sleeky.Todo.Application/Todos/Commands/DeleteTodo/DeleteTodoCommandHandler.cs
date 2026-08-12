using MediatR;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Todos.Commands.DeleteTodo;

public sealed class DeleteTodoCommandHandler : IRequestHandler<DeleteTodoCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ITodoRepository todoRepository;

    public DeleteTodoCommandHandler(ITodoRepository todoRepository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(clock);

        this.todoRepository = todoRepository;
        this.clock = clock;
    }

    public async Task<TodoDto> Handle(
        DeleteTodoCommand request,
        CancellationToken cancellationToken)
    {
        TodoItem todoItem = await todoRepository.GetByIdAsync(
            request.Id,
            cancellationToken: cancellationToken)
            ?? throw new NotFoundException("TODO", request.Id);

        TodoVersionGuard.EnsureExpectedVersion(todoItem, request.Version);
        bool hasActiveDependents = await todoRepository.HasActiveDependentsAsync(
            todoItem.Id,
            cancellationToken);
        if (hasActiveDependents)
        {
            throw new DomainException(
                "A TODO with active dependents cannot be deleted.");
        }

        todoItem.SoftDelete(clock.UtcNow);

        TodoItem deletedTodoItem = await todoRepository.SoftDeleteAsync(
            todoItem,
            request.Version,
            cancellationToken)
            ?? throw new ConcurrencyConflictException("TODO", request.Id, request.Version);

        return TodoDto.FromEntity(deletedTodoItem);
    }
}
