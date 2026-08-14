using MediatR;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Services;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

public sealed class GetTodosQueryHandler
    : IRequestHandler<GetTodosQuery, CursorPage<TodoListItemDto>>
{
    private readonly ITodoListReader todoListReader;

    public GetTodosQueryHandler(ITodoListReader todoListReader)
    {
        ArgumentNullException.ThrowIfNull(todoListReader);

        this.todoListReader = todoListReader;
    }

    public async Task<CursorPage<TodoListItemDto>> Handle(
        GetTodosQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> searchTerms = SearchTokenizer.Tokenize(request.SearchText);

        // The same tokenizer the write path uses, so a term and the token it
        // has to reach are produced by one set of rules. Punctuation-only text
        // yields nothing and therefore filters nothing, rather than matching
        // nothing.
        string filterSignature = TodoCursorCodec.CreateFilterSignature(request, searchTerms);
        TodoCursorPayload? cursor = request.Cursor is null
            ? null
            : TodoCursorCodec.Decode(request.Cursor);

        if (cursor is not null)
        {
            TodoCursorCodec.ValidateForQuery(cursor, request, filterSignature);
        }

        TodoListCriteria criteria = new TodoListCriteria(
            request.Status,
            request.Priority,
            request.DueFrom,
            request.DueTo,
            request.DependencyStatus,
            request.Scope,
            request.SortField,
            request.SortDirection,
            request.Limit + 1,
            cursor?.LastSortValue,
            cursor?.LastTodoId,
            searchTerms);
        IReadOnlyList<TodoListItemDto> results = await todoListReader.GetTodosAsync(
            criteria,
            cancellationToken);
        bool hasNextPage = results.Count > request.Limit;
        IReadOnlyList<TodoListItemDto> items = results
            .Take(request.Limit)
            .ToArray();
        string? nextCursor = hasNextPage
            ? TodoCursorCodec.Encode(
                TodoCursorCodec.Create(request, items[^1], filterSignature))
            : null;

        return new CursorPage<TodoListItemDto>(items, nextCursor);
    }
}
