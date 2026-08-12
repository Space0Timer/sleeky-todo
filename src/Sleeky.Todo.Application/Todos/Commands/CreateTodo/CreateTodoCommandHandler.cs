using MediatR;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Commands.CreateTodo;

public sealed class CreateTodoCommandHandler : IRequestHandler<CreateTodoCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ITodoRepository todoRepository;

    public CreateTodoCommandHandler(ITodoRepository todoRepository, IClock clock)
    {
        this.todoRepository = todoRepository;
        this.clock = clock;
    }

    public async Task<TodoDto> Handle(
        CreateTodoCommand request,
        CancellationToken cancellationToken)
    {
        TodoItem todoItem = TodoItem.Create(
            Guid.NewGuid().ToString("N"),
            request.Name,
            request.Description,
            request.DueDate,
            request.Priority,
            clock.UtcNow);

        await todoRepository.AddAsync(todoItem, cancellationToken);

        return TodoDto.FromEntity(todoItem);
    }
}
