using MediatR;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Commands.UpdateTodo;

public sealed class UpdateTodoCommandHandler : IRequestHandler<UpdateTodoCommand, TodoDto>
{
    private readonly IClock clock;
    private readonly ITodoRepository todoRepository;

    public UpdateTodoCommandHandler(ITodoRepository todoRepository, IClock clock)
    {
        this.todoRepository = todoRepository;
        this.clock = clock;
    }

    public async Task<TodoDto> Handle(
        UpdateTodoCommand request,
        CancellationToken cancellationToken)
    {
        TodoItem todoItem = await todoRepository.GetByIdAsync(
            request.Id,
            cancellationToken: cancellationToken)
            ?? throw new TodoNotFoundException(request.Id);

        TodoVersionGuard.EnsureExpectedVersion(todoItem, request.Version);

        todoItem.UpdateDetails(
            request.Name,
            request.Description,
            request.DueDate,
            request.Priority,
            clock.UtcNow);

        TodoItem updatedTodoItem = await todoRepository.UpdateAsync(
            todoItem,
            request.Version,
            cancellationToken)
            ?? throw new TodoConcurrencyException(request.Id, request.Version);

        return TodoDto.FromEntity(updatedTodoItem);
    }
}
