using MediatR;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;

public sealed class GetTodoSelectionQueryHandler
    : IRequestHandler<GetTodoSelectionQuery, TodoSelection>
{
    private readonly ITodoRepository todoRepository;

    public GetTodoSelectionQueryHandler(ITodoRepository todoRepository)
    {
        ArgumentNullException.ThrowIfNull(todoRepository);

        this.todoRepository = todoRepository;
    }

    public async Task<TodoSelection> Handle(
        GetTodoSelectionQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<TodoItem> loaded = await todoRepository.GetByIdsAsync(
            request.Ids,
            cancellationToken: cancellationToken);
        Dictionary<Guid, TodoItem> todosById = loaded.ToDictionary(todoItem => todoItem.Id);

        TodoDto[] items = request.Ids
            .Where(todosById.ContainsKey)
            .Select(id => TodoDto.FromEntity(todosById[id]))
            .ToArray();

        return new TodoSelection(items);
    }
}
