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
        // Soft-deleted TODOs are reported: they still exist, the trash lists
        // them, and a selection there has to be diffable like any other. Only
        // what is purged or outside this Space is absent.
        IReadOnlyCollection<TodoItem> loaded = await todoRepository.GetByIdsAsync(
            request.Ids,
            includeDeleted: true,
            cancellationToken);
        Dictionary<Guid, TodoItem> todosById = loaded.ToDictionary(todoItem => todoItem.Id);

        return new TodoSelection(SelectFound(request.Ids, todosById));
    }

    /// <summary>
    /// The TODOs that came back, in the order they were asked for, with the
    /// unresolved identifiers simply absent.
    /// </summary>
    /// <remarks>
    /// One lookup per identifier: testing membership and then reading the
    /// value searches the same dictionary twice for every TODO that was
    /// found, which is every TODO in the ordinary case.
    /// </remarks>
    private static TodoDto[] SelectFound(
        IReadOnlyCollection<Guid> requestedIds,
        Dictionary<Guid, TodoItem> todosById)
    {
        List<TodoDto> found = new List<TodoDto>(requestedIds.Count);

        foreach (Guid id in requestedIds)
        {
            if (todosById.TryGetValue(id, out TodoItem? todoItem))
            {
                found.Add(TodoDto.FromEntity(todoItem));
            }
        }

        return found.ToArray();
    }
}
