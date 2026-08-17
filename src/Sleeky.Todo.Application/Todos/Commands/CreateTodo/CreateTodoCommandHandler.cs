using MediatR;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Todos.Commands.CreateTodo;

public sealed class CreateTodoCommandHandler : IRequestHandler<CreateTodoCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly ILogger<CreateTodoCommandHandler> logger;
    private readonly ITodoRepository todoRepository;

    public CreateTodoCommandHandler(
        ITodoRepository todoRepository,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<CreateTodoCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoRepository = todoRepository;
        this.clock = clock;
        this.currentUser = currentUser;
        this.logger = logger;
    }

    public async Task<TodoDto> Handle(
        CreateTodoCommand request,
        CancellationToken cancellationToken)
    {
        RecurrenceSchedule? recurrence = BuildRecurrence(request);

        // A recurring TODO is the first occurrence of a new series, so it mints
        // the series identifier its successors will share and takes occurrence
        // number one. A one-off carries neither; the entity refuses a half-set
        // combination.
        TodoItem todoItem = TodoItem.Create(
            request.Id ?? Guid.NewGuid(),
            request.SpaceId,
            currentUser.UserId,
            request.Name,
            request.Description,
            request.DueDate,
            request.Priority,
            clock.UtcNow,
            recurrence,
            seriesId: recurrence is null ? null : Guid.NewGuid(),
            occurrenceNumber: recurrence is null ? null : 1);

        await todoRepository.AddAsync(todoItem, cancellationToken);

        this.logger.LogInformation(
            1102,
            "Created TODO {TodoId} for series {SeriesId} at version {Version}",
            todoItem.Id,
            todoItem.SeriesId,
            todoItem.Version);

        return TodoDto.FromEntity(todoItem);
    }

    /// <summary>
    /// A schedule anchored on the first due date, or null for a one-off.
    /// </summary>
    private static RecurrenceSchedule? BuildRecurrence(CreateTodoCommand request)
    {
        if (!request.RecurrenceType.HasValue)
        {
            return null;
        }

        return RecurrenceSchedule.Create(
            request.RecurrenceType.Value,
            request.RecurrenceInterval ?? 1,
            request.RecurrenceUnit,
            request.DueDate);
    }
}
