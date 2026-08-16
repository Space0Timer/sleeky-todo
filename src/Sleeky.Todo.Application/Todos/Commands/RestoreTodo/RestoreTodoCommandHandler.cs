using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Commands.RestoreTodo;

public sealed class RestoreTodoCommandHandler : IRequestHandler<RestoreTodoCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ILogger<RestoreTodoCommandHandler> logger;
    private readonly ITodoRepository todoRepository;

    public RestoreTodoCommandHandler(
        ITodoRepository todoRepository,
        IClock clock,
        ILogger<RestoreTodoCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<TodoDto> Handle(
        RestoreTodoCommand request,
        CancellationToken cancellationToken)
    {
        // The target is deleted by definition, so this is the one single-item
        // command that has to look past the soft-delete filter to find it.
        TodoItem todoItem = await todoRepository.GetByIdAsync(
            request.Id,
            includeDeleted: true,
            cancellationToken)
            ?? throw new NotFoundException("TODO", request.Id);

        TodoVersionGuard.EnsureExpectedVersion(todoItem, request.Version);

        // Restoration has no dependency gate: a restored TODO blocks nothing,
        // and its own prerequisites are evaluated when it next changes status.
        // The retention rule is the entity's own.
        todoItem.Restore(clock.UtcNow);

        TodoItem restoredTodoItem = await todoRepository.RestoreAsync(
            todoItem,
            cancellationToken);

        this.logger.LogInformation(
            1107,
            "Restored TODO {TodoId} at version {Version}",
            restoredTodoItem.Id,
            restoredTodoItem.Version);

        return TodoDto.FromEntity(restoredTodoItem);
    }
}
