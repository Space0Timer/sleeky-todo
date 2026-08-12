using MediatR;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Commands.RestoreTodo;

public sealed class RestoreTodoCommandHandler : IRequestHandler<RestoreTodoCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ITodoRepository todoRepository;

    public RestoreTodoCommandHandler(ITodoRepository todoRepository, IClock clock)
    {
        this.todoRepository = todoRepository;
        this.clock = clock;
    }

    public async Task<TodoDto> Handle(
        RestoreTodoCommand request,
        CancellationToken cancellationToken)
    {
        TodoItem todoItem = await todoRepository.GetByIdAsync(
            request.Id,
            includeDeleted: true,
            cancellationToken)
            ?? throw new NotFoundException("TODO", request.Id);

        TodoVersionGuard.EnsureExpectedVersion(todoItem, request.Version);
        todoItem.Restore(clock.UtcNow);

        TodoItem restoredTodoItem = await todoRepository.RestoreAsync(
            todoItem,
            request.Version,
            cancellationToken)
            ?? throw new ConcurrencyConflictException("TODO", request.Id, request.Version);

        return TodoDto.FromEntity(restoredTodoItem);
    }
}
