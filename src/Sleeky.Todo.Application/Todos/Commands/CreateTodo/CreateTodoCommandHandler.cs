using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Todos.Commands.CreateTodo;

public sealed class CreateTodoCommandHandler : IRequestHandler<CreateTodoCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ILogger<CreateTodoCommandHandler> logger;
    private readonly ITodoRepository todoRepository;

    public CreateTodoCommandHandler(
        ITodoRepository todoRepository,
        IClock clock,
        ILogger<CreateTodoCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<TodoDto> Handle(
        CreateTodoCommand request,
        CancellationToken cancellationToken)
    {
        RecurrenceSchedule? recurrence = request.RecurrenceType.HasValue
            ? RecurrenceSchedule.Create(
                request.RecurrenceType.Value,
                request.RecurrenceInterval ?? 1,
                request.RecurrenceUnit,
                request.DueDate)
            : null;
        TodoItem todoItem = TodoItem.Create(
            Guid.NewGuid(),
            request.Name,
            request.Description,
            request.DueDate,
            request.Priority,
            clock.UtcNow,
            recurrence,
            recurrence is null ? null : Guid.NewGuid(),
            recurrence is null ? null : 1);

        await todoRepository.AddAsync(todoItem, cancellationToken);

        this.logger.LogInformation(
            1102,
            "Created TODO {TodoId} for series {SeriesId} at version {Version}",
            todoItem.Id,
            todoItem.SeriesId,
            todoItem.Version);

        return TodoDto.FromEntity(todoItem);
    }
}
