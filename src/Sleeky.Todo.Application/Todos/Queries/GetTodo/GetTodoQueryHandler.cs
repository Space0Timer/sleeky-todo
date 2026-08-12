using MediatR;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodo;

public sealed class GetTodoQueryHandler : IRequestHandler<GetTodoQuery, TodoDto>
{
    private readonly ITodoRepository todoRepository;

    public GetTodoQueryHandler(ITodoRepository todoRepository)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);

        this.todoRepository = todoRepository;
    }

    public async Task<TodoDto> Handle(
        GetTodoQuery request,
        CancellationToken cancellationToken)
    {
        TodoItem todoItem = await todoRepository.GetByIdAsync(
            request.Id,
            cancellationToken: cancellationToken)
            ?? throw new NotFoundException("TODO", request.Id);

        return TodoDto.FromEntity(todoItem);
    }
}
