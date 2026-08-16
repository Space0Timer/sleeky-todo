namespace Sleeky.Todo.Application.Todos.Queries.GetTodos;

/// <summary>
/// What a page cursor carries: enough to resume the same query exactly one row
/// after the last one returned. Serialised by <see cref="TodoCursorCodec"/>
/// into the opaque token the client hands back.
/// </summary>
/// <remarks>
/// A cursor is opaque to the client but not secret: it is not signed, so its
/// contents are validated on decode as untrusted input, and its filter
/// signature is what stops a cursor from one query being replayed against
/// another.
/// </remarks>
public sealed record TodoCursorPayload
{
    /// <summary>
    /// The version of this payload's shape — not a TODO's concurrency version.
    /// A cursor written by an older format is refused rather than
    /// misinterpreted.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// The persisted name of the field the page was sorted by.
    /// </summary>
    public string SortField { get; init; } = string.Empty;

    /// <summary>
    /// <c>asc</c> or <c>desc</c>.
    /// </summary>
    public string Direction { get; init; } = string.Empty;

    /// <summary>
    /// The sort key of the last item on the page, as text. A string because the
    /// key's type follows the sort field — a date, an integer rank, or a
    /// lowercase name — and the codec re-parses it for the field in play.
    /// </summary>
    public string LastSortValue { get; init; } = string.Empty;

    /// <summary>
    /// The identifier of the last item on the page — the tie-breaker that keeps
    /// paging deterministic when several items share a sort key.
    /// </summary>
    public Guid LastTodoId { get; init; }

    /// <summary>
    /// A hash of the filters, scope, and search terms the page was produced
    /// under, so a cursor cannot continue a different question.
    /// </summary>
    public string FilterSignature { get; init; } = string.Empty;
}
