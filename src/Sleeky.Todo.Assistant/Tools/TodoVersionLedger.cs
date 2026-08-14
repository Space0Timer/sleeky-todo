using Sleeky.Todo.Application.Todos.Commands.Bulk;

namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// Remembers the version of every TODO the model has read, so a write can send
/// the version the actor last saw rather than whatever the store holds now.
/// </summary>
/// <remarks>
/// This is why the write tools take identifiers only. Letting the model supply
/// versions would put a value it can invent on the concurrency check, and
/// letting the tool layer read one immediately before writing would be a blind
/// overwrite wearing an optimistic check. Neither is what the browser does, and
/// the browser is the standard this holds itself to.
/// </remarks>
public sealed class TodoVersionLedger
{
    private readonly Dictionary<Guid, long> versions = new Dictionary<Guid, long>();

    public void Record(Guid id, long version)
    {
        this.versions[id] = version;
    }

    public void RecordRange(IEnumerable<TodoSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        foreach (TodoSummary summary in summaries)
        {
            this.Record(summary.Id, summary.Version);
        }
    }

    /// <summary>
    /// Binds identifiers to the versions they were last read at. Anything never
    /// read is reported rather than guessed, which the caller turns into an
    /// instruction to read first.
    /// </summary>
    public bool TryBind(
        IReadOnlyCollection<Guid> ids,
        out IReadOnlyCollection<BulkTodoItemRequest> bound,
        out IReadOnlyCollection<Guid> unread)
    {
        ArgumentNullException.ThrowIfNull(ids);

        List<BulkTodoItemRequest> items = new List<BulkTodoItemRequest>(ids.Count);
        List<Guid> missing = new List<Guid>();

        foreach (Guid id in ids)
        {
            if (this.versions.TryGetValue(id, out long version))
            {
                items.Add(new BulkTodoItemRequest(id, version));
                continue;
            }

            missing.Add(id);
        }

        bound = items;
        unread = missing;

        return missing.Count == 0;
    }
}
