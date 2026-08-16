using MediatR;

using Microsoft.Extensions.Logging;

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
    private readonly ILogger<DeleteTodoCommandHandler> logger;
    private readonly ITodoRepository todoRepository;

    public DeleteTodoCommandHandler(
        ITodoRepository todoRepository,
        IClock clock,
        ILogger<DeleteTodoCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.clock = clock;
        this.logger = logger;
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
        await EnsureNoActiveDependentsAsync(todoItem.Id, cancellationToken);

        todoItem.SoftDelete(clock.UtcNow);

        TodoItem deletedTodoItem = await todoRepository.SoftDeleteAsync(
            todoItem,
            cancellationToken);

        this.logger.LogInformation(
            1106,
            "Deleted TODO {TodoId} at version {Version}; scheduled purge at {PurgeAt}",
            deletedTodoItem.Id,
            deletedTodoItem.Version,
            deletedTodoItem.PurgeAt);

        return TodoDto.FromEntity(deletedTodoItem);
    }

    /// <summary>
    /// A prerequisite of an active dependent cannot be deleted: the dependent
    /// would be left waiting on something that no longer resolves. Dependents
    /// that are themselves deleted or archived do not count. Whether other TODOs
    /// depend on this one is not visible from the entity, so it is checked
    /// here.
    /// </summary>
    private async Task EnsureNoActiveDependentsAsync(
        Guid todoId,
        CancellationToken cancellationToken)
    {
        bool hasActiveDependents = await todoRepository.HasActiveDependentsAsync(
            todoId,
            cancellationToken);

        if (hasActiveDependents)
        {
            throw new DomainException(
                "A TODO with active dependents cannot be deleted.");
        }
    }
}
