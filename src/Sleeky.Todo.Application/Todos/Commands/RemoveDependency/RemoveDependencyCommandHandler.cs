using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
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
        TodoItem todoItem = await todoRepository.GetRequiredAsync(
            request.Id,
            request.Version,
            cancellationToken);

        // Whether the edge exists, and whether this TODO may still be edited,
        // are the entity's rules; there is no other TODO to consult here.
        todoItem.RemoveDependency(request.DependencyId, clock.UtcNow);

        TodoItem updatedTodo = await todoRepository.UpdateAsync(
            todoItem,
            cancellationToken);

        this.logger.LogInformation(
            1105,
            "Removed dependency {DependencyTodoId} from TODO {TodoId} at version {Version}",
            request.DependencyId,
            updatedTodo.Id,
            updatedTodo.Version);

        return TodoDto.FromEntity(updatedTodo);
    }
}
