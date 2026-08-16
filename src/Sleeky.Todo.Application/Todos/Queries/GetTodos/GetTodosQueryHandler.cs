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
        // The same tokenizer the write path uses, so a term and the token it
        // has to reach are produced by one set of rules. Punctuation-only text
        // yields nothing and therefore filters nothing, rather than matching
        // nothing.
        IReadOnlyList<string> searchTerms = SearchTokenizer.Tokenize(request.SearchText);
        string filterSignature = TodoCursorCodec.CreateFilterSignature(request, searchTerms);
        TodoCursorPayload? cursor = DecodeCursor(request, filterSignature);

        TodoListCriteria criteria = BuildCriteria(request, cursor, searchTerms);
        IReadOnlyList<TodoListItemDto> results = await todoListReader.GetTodosAsync(
            criteria,
            cancellationToken);

        return BuildPage(request, results, filterSignature);
    }

    /// <summary>
    /// A cursor is accepted only if it was issued for the same sort, scope, and
    /// filters, so a page cannot be continued under a different question.
    /// </summary>
    private static TodoCursorPayload? DecodeCursor(
        GetTodosQuery request,
        string filterSignature)
    {
        if (request.Cursor is null)
        {
            return null;
        }

        TodoCursorPayload cursor = TodoCursorCodec.Decode(request.Cursor);
        TodoCursorCodec.ValidateForQuery(cursor, request, filterSignature);

        return cursor;
    }

    /// <summary>
    /// Asks for one row past the page so the reader, not a count, decides
    /// whether a next page exists.
    /// </summary>
    private static TodoListCriteria BuildCriteria(
        GetTodosQuery request,
        TodoCursorPayload? cursor,
        IReadOnlyList<string> searchTerms)
    {
        return new TodoListCriteria(
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
    }

    /// <summary>
    /// Trims the sentinel row and, when it was present, issues a cursor that
    /// resumes after the last item actually returned.
    /// </summary>
    private static CursorPage<TodoListItemDto> BuildPage(
        GetTodosQuery request,
        IReadOnlyList<TodoListItemDto> results,
        string filterSignature)
    {
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
