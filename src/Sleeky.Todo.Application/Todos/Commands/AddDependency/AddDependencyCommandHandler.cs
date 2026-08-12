using MediatR;

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
    private readonly ITodoRepository todoRepository;

    public AddDependencyCommandHandler(
        ITodoRepository todoRepository,
        IDependencyGraphService dependencyGraphService,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(dependencyGraphService);
        ArgumentNullException.ThrowIfNull(clock);

        this.todoRepository = todoRepository;
        this.dependencyGraphService = dependencyGraphService;
        this.clock = clock;
    }

    public async Task<TodoDto> Handle(
        AddDependencyCommand request,
        CancellationToken cancellationToken)
    {
        TodoItem todoItem = await todoRepository.GetByIdAsync(
            request.Id,
            cancellationToken: cancellationToken)
            ?? throw new NotFoundException("TODO", request.Id);
        TodoVersionGuard.EnsureExpectedVersion(todoItem, request.Version);

        if (string.Equals(todoItem.Id, request.DependencyId, StringComparison.Ordinal))
        {
            todoItem.AddDependency(request.DependencyId, clock.UtcNow);
        }

        _ = await todoRepository.GetByIdAsync(
            request.DependencyId,
            cancellationToken: cancellationToken)
            ?? throw new NotFoundException("Dependency TODO", request.DependencyId);

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
            request.Version,
            cancellationToken)
            ?? throw new ConcurrencyConflictException("TODO", request.Id, request.Version);

        return TodoDto.FromEntity(updatedTodo);
    }
}
