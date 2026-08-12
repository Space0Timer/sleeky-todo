using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Commands.RemoveDependency;

public sealed class RemoveDependencyCommandHandler
    : IRequestHandler<RemoveDependencyCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ILogger<RemoveDependencyCommandHandler> logger;
    private readonly ITodoRepository todoRepository;

    public RemoveDependencyCommandHandler(
        ITodoRepository todoRepository,
        IClock clock,
        ILogger<RemoveDependencyCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<TodoDto> Handle(
        RemoveDependencyCommand request,
        CancellationToken cancellationToken)
    {
        TodoItem todoItem = await todoRepository.GetByIdAsync(
            request.Id,
            cancellationToken: cancellationToken)
            ?? throw new NotFoundException("TODO", request.Id);
        TodoVersionGuard.EnsureExpectedVersion(todoItem, request.Version);
        todoItem.RemoveDependency(request.DependencyId, clock.UtcNow);

        TodoItem updatedTodo = await todoRepository.UpdateAsync(
            todoItem,
            request.Version,
            cancellationToken)
            ?? throw new ConcurrencyConflictException("TODO", request.Id, request.Version);

        this.logger.LogInformation(
            1105,
            "Removed dependency {DependencyTodoId} from TODO {TodoId} at version {Version}",
            request.DependencyId,
            updatedTodo.Id,
            updatedTodo.Version);

        return TodoDto.FromEntity(updatedTodo);
    }
}
