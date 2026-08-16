using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Todos.Commands.AddDependency;

public sealed class AddDependencyCommandHandler
    : IRequestHandler<AddDependencyCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly IDependencyCycleDetector cycleDetector;
    private readonly ILogger<AddDependencyCommandHandler> logger;
    private readonly ITodoRepository todoRepository;

    public AddDependencyCommandHandler(
        ITodoRepository todoRepository,
        IDependencyCycleDetector cycleDetector,
        IClock clock,
        ILogger<AddDependencyCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(cycleDetector);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.cycleDetector = cycleDetector;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<TodoDto> Handle(
        AddDependencyCommand request,
        CancellationToken cancellationToken)
    {
        // Only this end is mutated and persisted, so only this end is loaded
        // whole.
        TodoItem todoItem = await todoRepository.GetRequiredAsync(
            request.Id,
            request.Version,
            cancellationToken);

        // Ahead of the existence check, which a self dependency would pass, and
        // ahead of the cycle check, which would report it as a cycle instead: a
        // node is trivially reachable from itself.
        if (todoItem.Id == request.DependencyId)
        {
            throw new DomainException("A TODO cannot depend on itself.");
        }

        await EnsureDependencyExistsAsync(request.DependencyId, cancellationToken);
        await EnsureNoCycleAsync(todoItem.Id, request.DependencyId, cancellationToken);

        todoItem.AddDependency(request.DependencyId, clock.UtcNow);
        TodoItem updatedTodo = await todoRepository.UpdateAsync(
            todoItem,
            cancellationToken);

        this.logger.LogInformation(
            1104,
            "Added dependency {DependencyTodoId} to TODO {TodoId} at version {Version}",
            request.DependencyId,
            updatedTodo.Id,
            updatedTodo.Version);

        return TodoDto.FromEntity(updatedTodo);
    }

    /// <summary>
    /// The other end is only ever asked whether it exists, so it is counted
    /// rather than fetched.
    /// </summary>
    private async Task EnsureDependencyExistsAsync(
        Guid dependencyId,
        CancellationToken cancellationToken)
    {
        bool dependencyExists = await todoRepository.ExistsAsync(
            dependencyId,
            cancellationToken: cancellationToken);

        if (!dependencyExists)
        {
            throw new NotFoundException("Dependency TODO", dependencyId);
        }
    }

    private async Task EnsureNoCycleAsync(
        Guid todoId,
        Guid dependencyId,
        CancellationToken cancellationToken)
    {
        bool createsCycle = await cycleDetector.WouldCreateCycleAsync(
            todoId,
            dependencyId,
            cancellationToken);

        if (createsCycle)
        {
            throw new DomainException("Adding this dependency would create a cycle.");
        }
    }
}
