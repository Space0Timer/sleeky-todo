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
    private readonly IDependencyGraphService dependencyGraphService;
    private readonly ILogger<AddDependencyCommandHandler> logger;
    private readonly ITodoRepository todoRepository;

    public AddDependencyCommandHandler(
        ITodoRepository todoRepository,
        IDependencyGraphService dependencyGraphService,
        IClock clock,
        ILogger<AddDependencyCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(dependencyGraphService);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.dependencyGraphService = dependencyGraphService;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<TodoDto> Handle(
        AddDependencyCommand request,
        CancellationToken cancellationToken)
    {
        // Only this end is mutated and persisted, so only this end is loaded
        // whole.
        TodoItem todoItem = await todoRepository.GetByIdAsync(
            request.Id,
            cancellationToken: cancellationToken)
            ?? throw new NotFoundException("TODO", request.Id);
        TodoVersionGuard.EnsureExpectedVersion(todoItem, request.Version);

        // Ahead of the existence check, which a self dependency would pass, and
        // ahead of the cycle check, which would report it as a cycle instead: a
        // node is trivially reachable from itself.
        if (todoItem.Id == request.DependencyId)
        {
            throw new DomainException("A TODO cannot depend on itself.");
        }

        // The other end is only ever asked whether it exists, so it is counted
        // rather than fetched.
        bool dependencyExists = await todoRepository.ExistsAsync(
            request.DependencyId,
            cancellationToken: cancellationToken);
        if (!dependencyExists)
        {
            throw new NotFoundException("Dependency TODO", request.DependencyId);
        }

        bool createsCycle = await dependencyGraphService.WouldCreateCycleAsync(
            todoItem.Id,
            request.DependencyId,
            cancellationToken);
        if (createsCycle)
        {
            throw new DomainException("Adding this dependency would create a cycle.");
        }

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
}
