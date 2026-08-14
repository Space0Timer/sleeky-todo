using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Commands.UpdateTodo;

public sealed class UpdateTodoCommandHandler : IRequestHandler<UpdateTodoCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ILogger<UpdateTodoCommandHandler> logger;
    private readonly ITodoRepository todoRepository;

    public UpdateTodoCommandHandler(
        ITodoRepository todoRepository,
        IClock clock,
        ILogger<UpdateTodoCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<TodoDto> Handle(
        UpdateTodoCommand request,
        CancellationToken cancellationToken)
    {
        TodoItem todoItem = await todoRepository.GetByIdAsync(
            request.Id,
            cancellationToken: cancellationToken)
            ?? throw new NotFoundException("TODO", request.Id);

        TodoVersionGuard.EnsureExpectedVersion(todoItem, request.Version);

        todoItem.UpdateDetails(
            request.Name,
            request.Description,
            request.DueDate,
            request.Priority,
            clock.UtcNow);

        TodoItem updatedTodoItem = await todoRepository.UpdateAsync(
            todoItem,
            cancellationToken);

        this.logger.LogInformation(
            1103,
            "Updated TODO {TodoId} from version {PreviousVersion} to {Version}",
            updatedTodoItem.Id,
            request.Version,
            updatedTodoItem.Version);

        return TodoDto.FromEntity(updatedTodoItem);
    }
}
