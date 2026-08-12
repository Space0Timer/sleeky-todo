using MediatR;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Todos.Commands.CreateTodo;

public sealed class CreateTodoCommandHandler : IRequestHandler<CreateTodoCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ITodoRepository todoRepository;

    public CreateTodoCommandHandler(ITodoRepository todoRepository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);
        ArgumentNullException.ThrowIfNull(clock);

        this.todoRepository = todoRepository;
        this.clock = clock;
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
            Guid.NewGuid().ToString("N"),
            request.Name,
            request.Description,
            request.DueDate,
            request.Priority,
            clock.UtcNow,
            recurrence,
            recurrence is null ? null : Guid.NewGuid().ToString("N"),
            recurrence is null ? null : 1);

        await todoRepository.AddAsync(todoItem, cancellationToken);

        return TodoDto.FromEntity(todoItem);
    }
}
