using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Documents;
using Sleeky.Todo.Infrastructure.Persistence.Transactions;

namespace Sleeky.Todo.Infrastructure.Persistence.Repositories;

internal sealed class MongoTodoRepository : ITodoRepository
{
    private const int WriteConflictErrorCode = 112;

    /// <summary>
    /// Search tokens are derived, so nothing that reads a TODO back needs them.
    /// </summary>
    /// <remarks>
    /// <see cref="TodoDocumentMapper.ToDomain"/> never reads the field —
    /// <see cref="TodoItem"/> recomputes its tokens from the name and
    /// description — so every byte fetched here would be discarded. Excluding
    /// them is safe across a read-modify-write because writes go back through
    /// <see cref="TodoDocumentMapper.FromDomain"/>, which recomputes the field
    /// rather than echoing what was read.
    /// </remarks>
    private static readonly ProjectionDefinition<TodoDocument> WithoutSearchTokens =
        Builders<TodoDocument>.Projection.Exclude(document => document.SearchTokens);

    /// <summary>
    /// The fields <see cref="TodoDependencyNode"/> is built from.
    /// </summary>
    /// <remarks>
    /// An include projection, unlike <see cref="WithoutSearchTokens"/>, because
    /// dependency reasoning wants the short list rather than everything but one
    /// field. <see cref="TodoDocument"/> is
    /// <see cref="MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElementsAttribute"/>
    /// and every property carries a default, so the fields left out deserialise
    /// harmlessly and are never read.
    /// </remarks>
    private static readonly ProjectionDefinition<TodoDocument> DependencyFields =
        Builders<TodoDocument>.Projection
            .Include(document => document.Id)
            .Include(document => document.Status)
            .Include(document => document.DeletedAt)
            .Include(document => document.DependencyIds);

    private readonly ICurrentUser currentUser;
    private readonly SessionAwareCollection<TodoDocument> todoItems;

    public MongoTodoRepository(
        IMongoCollection<TodoDocument> todoItems,
        ICurrentUser currentUser,
        MongoTransactionContext? transactionContext = null)
    {
        ArgumentNullException.ThrowIfNull(todoItems);
        ArgumentNullException.ThrowIfNull(currentUser);

        this.todoItems = new SessionAwareCollection<TodoDocument>(
            todoItems,
            transactionContext ?? new MongoTransactionContext());
        this.currentUser = currentUser;
    }

    private Guid OwnerId => currentUser.UserId;

    public async Task AddAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todoItem);
        EnsureOwned(todoItem);

        TodoDocument document = TodoDocumentMapper.FromDomain(todoItem);
        await todoItems.InsertOneAsync(document, cancellationToken);
    }

    public async Task<TodoItem?> GetByIdAsync(
        Guid id,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<TodoDocument> filter = BuildIdFilter(id, includeDeleted);
        TodoDocument? document = await todoItems
            .Find(filter)
            .Project<TodoDocument>(WithoutSearchTokens)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : TodoDocumentMapper.ToDomain(document);
    }

    public async Task<bool> ExistsAsync(
        Guid id,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<TodoDocument> filter = BuildIdFilter(id, includeDeleted);
        long count = await todoItems.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public async Task<IReadOnlyCollection<TodoItem>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        Guid[] distinctIds = ids.Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return Array.Empty<TodoItem>();
        }

        FilterDefinition<TodoDocument> filter = BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.In(document => document.Id, distinctIds);
        if (!includeDeleted)
        {
            filter &= Builders<TodoDocument>.Filter.Eq(document => document.DeletedAt, null);
        }

        List<TodoDocument> documents = await todoItems
            .Find(filter)
            .Project<TodoDocument>(WithoutSearchTokens)
            .ToListAsync(cancellationToken);

        return documents.Select(TodoDocumentMapper.ToDomain).ToArray();
    }

    public async Task<IReadOnlyCollection<TodoDependencyNode>> GetDependencyNodesAsync(
        IEnumerable<Guid> ids,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        Guid[] distinctIds = ids.Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return Array.Empty<TodoDependencyNode>();
        }

        FilterDefinition<TodoDocument> filter = BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.In(document => document.Id, distinctIds);
        if (!includeDeleted)
        {
            filter &= Builders<TodoDocument>.Filter.Eq(document => document.DeletedAt, null);
        }

        List<TodoDocument> documents = await todoItems
            .Find(filter)
            .Project<TodoDocument>(DependencyFields)
            .ToListAsync(cancellationToken);

        return documents
            .Select(document => new TodoDependencyNode(
                document.Id,
                document.Status,
                document.DeletedAt is not null,
                document.DependencyIds))
            .ToArray();
    }

    public async Task<bool> HasActiveDependentsAsync(
        Guid dependencyId,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<TodoDocument> filter = BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.AnyEq(
                document => document.DependencyIds,
                dependencyId)
            & Builders<TodoDocument>.Filter.Eq(document => document.DeletedAt, null)
            & Builders<TodoDocument>.Filter.Ne(
                document => document.Status,
                TodoStatus.Archived);
        long count = await todoItems.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public async Task<IReadOnlyCollection<Guid>> GetActiveDependentIdsAsync(
        IReadOnlyCollection<Guid> dependencyIds,
        IReadOnlyCollection<Guid> excludedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencyIds);
        ArgumentNullException.ThrowIfNull(excludedIds);

        if (dependencyIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        FilterDefinition<TodoDocument> filter = BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.AnyIn(
                document => document.DependencyIds,
                dependencyIds)
            & Builders<TodoDocument>.Filter.Eq(document => document.DeletedAt, null)
            & Builders<TodoDocument>.Filter.Ne(
                document => document.Status,
                TodoStatus.Archived)
            & Builders<TodoDocument>.Filter.Nin(document => document.Id, excludedIds);
        FindOptions<TodoDocument, TodoDocument> options =
            new FindOptions<TodoDocument, TodoDocument>
            {
                Projection = Builders<TodoDocument>.Projection.Include(
                    document => document.Id),
            };
        using IAsyncCursor<TodoDocument> cursor = await todoItems.FindAsync(
            filter,
            options,
            cancellationToken);
        List<TodoDocument> documents = await cursor.ToListAsync(cancellationToken);

        return documents.Select(document => document.Id).ToArray();
    }

    public Task<TodoItem> UpdateAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        FilterDefinition<TodoDocument> filter = BuildMutationFilter(
            todoItem.Id,
            todoItem.Version,
            includeDeleted: false);

        return ReplaceAsync(todoItem, filter, cancellationToken);
    }

    public Task<TodoItem> SoftDeleteAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        if (todoItem.DeletedAt is null || todoItem.PurgeAt is null)
        {
            throw new InvalidOperationException(
                "A TODO must be soft-deleted before it can be persisted as deleted.");
        }

        FilterDefinition<TodoDocument> filter = BuildMutationFilter(
            todoItem.Id,
            todoItem.Version,
            includeDeleted: false);

        return ReplaceAsync(todoItem, filter, cancellationToken);
    }

    public Task<TodoItem> RestoreAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        if (todoItem.DeletedAt is not null || todoItem.PurgeAt is not null)
        {
            throw new InvalidOperationException(
                "A TODO must be restored before it can be persisted as active.");
        }

        FilterDefinition<TodoDocument> filter = BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.Eq(document => document.Id, todoItem.Id)
            & Builders<TodoDocument>.Filter.Eq(document => document.Version, todoItem.Version)
            & Builders<TodoDocument>.Filter.Ne(document => document.DeletedAt, null);

        return ReplaceAsync(todoItem, filter, cancellationToken);
    }

    /// <summary>
    /// Applies every write as one ordered bulk operation. Each replacement keeps
    /// the versioned filter a single write would use, and the matched count is
    /// verified afterwards, so a batch either applies in full or — when it runs
    /// inside a transaction — not at all.
    /// </summary>
    public async Task SaveBatchAsync(
        IReadOnlyCollection<TodoItem> updates,
        IReadOnlyCollection<TodoItem> inserts,
        CancellationToken cancellationToken = default,
        bool expectDeleted = false)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(inserts);

        if (updates.Count == 0 && inserts.Count == 0)
        {
            return;
        }

        List<WriteModel<TodoDocument>> writes =
            new List<WriteModel<TodoDocument>>(updates.Count + inserts.Count);
        List<Guid> writtenIds = new List<Guid>(updates.Count + inserts.Count);

        foreach (TodoItem todoItem in updates)
        {
            EnsureOwned(todoItem);
            writes.Add(new ReplaceOneModel<TodoDocument>(
                BuildBatchMutationFilter(todoItem.Id, todoItem.Version, expectDeleted),
                TodoDocumentMapper.FromDomain(
                    todoItem,
                    checked(todoItem.Version + 1))));
            writtenIds.Add(todoItem.Id);
        }

        foreach (TodoItem todoItem in inserts)
        {
            EnsureOwned(todoItem);
            writes.Add(new InsertOneModel<TodoDocument>(
                TodoDocumentMapper.FromDomain(todoItem)));
            writtenIds.Add(todoItem.Id);
        }

        BulkWriteOptions options = new BulkWriteOptions { IsOrdered = true };
        BulkWriteResult<TodoDocument> result;
        try
        {
            result = await todoItems.BulkWriteAsync(writes, options, cancellationToken);
        }
        catch (MongoBulkWriteException exception)
            when (GetConflictingIds(exception, writtenIds) is { Count: > 0 } conflictingIds)
        {
            // A bulk write reports its own errors rather than raising the single
            // write exception the transaction executor classifies, so duplicate
            // keys are translated here where the submitted models are known.
            throw new BulkConcurrencyConflictException("TODO", conflictingIds, exception);
        }

        if (result.MatchedCount != updates.Count || result.InsertedCount != inserts.Count)
        {
            throw new BulkConcurrencyConflictException(
                "TODO",
                updates.Select(todoItem => todoItem.Id).ToArray());
        }
    }

    private static IReadOnlyCollection<Guid> GetConflictingIds(
        MongoBulkWriteException exception,
        IReadOnlyList<Guid> writtenIds)
    {
        return exception.WriteErrors
            .Where(error => error.Category == ServerErrorCategory.DuplicateKey
                || error.Code == WriteConflictErrorCode)
            .Select(error => error.Index)
            .Where(index => index >= 0 && index < writtenIds.Count)
            .Select(index => writtenIds[index])
            .Distinct()
            .ToArray();
    }

    private void EnsureOwned(TodoItem todoItem)
    {
        if (todoItem.OwnerId != OwnerId)
        {
            throw new InvalidOperationException(
                "A TODO can only be persisted by its owner.");
        }
    }

    private FilterDefinition<TodoDocument> BuildOwnerFilter()
    {
        return Builders<TodoDocument>.Filter.Eq(document => document.OwnerId, OwnerId);
    }

    private FilterDefinition<TodoDocument> BuildIdFilter(
        Guid id,
        bool includeDeleted)
    {
        FilterDefinition<TodoDocument> filter = BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.Eq(document => document.Id, id);

        if (!includeDeleted)
        {
            filter &= Builders<TodoDocument>.Filter.Eq(document => document.DeletedAt, null);
        }

        return filter;
    }

    private FilterDefinition<TodoDocument> BuildMutationFilter(
        Guid id,
        long expectedVersion,
        bool includeDeleted)
    {
        return BuildIdFilter(id, includeDeleted)
            & Builders<TodoDocument>.Filter.Eq(document => document.Version, expectedVersion);
    }

    /// <summary>
    /// A restoring batch asserts the stored document is deleted, exactly as the
    /// single-item restore does, so a document someone else already restored
    /// fails the batch instead of being written over.
    /// </summary>
    private FilterDefinition<TodoDocument> BuildBatchMutationFilter(
        Guid id,
        long expectedVersion,
        bool expectDeleted)
    {
        if (!expectDeleted)
        {
            return BuildMutationFilter(id, expectedVersion, includeDeleted: false);
        }

        return BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.Eq(document => document.Id, id)
            & Builders<TodoDocument>.Filter.Eq(document => document.Version, expectedVersion)
            & Builders<TodoDocument>.Filter.Ne(document => document.DeletedAt, null);
    }

    /// <summary>
    /// Replaces the stored document only while it still carries the version the
    /// aggregate was loaded at, so a concurrent writer cannot be overwritten.
    /// </summary>
    private async Task<TodoItem> ReplaceAsync(
        TodoItem todoItem,
        FilterDefinition<TodoDocument> filter,
        CancellationToken cancellationToken)
    {
        long expectedVersion = todoItem.Version;
        long nextVersion = checked(expectedVersion + 1);
        TodoDocument replacement = TodoDocumentMapper.FromDomain(todoItem, nextVersion);

        // Without the projection the tokens just written travel straight back,
        // so a single write would pay for them twice on one round trip.
        FindOneAndReplaceOptions<TodoDocument> options = new FindOneAndReplaceOptions<TodoDocument>
        {
            ReturnDocument = ReturnDocument.After,
            Projection = WithoutSearchTokens,
        };
        TodoDocument? persistedDocument = await todoItems.FindOneAndReplaceAsync(
            filter,
            replacement,
            options,
            cancellationToken);

        return persistedDocument is null
            ? throw new ConcurrencyConflictException("TODO", todoItem.Id, expectedVersion)
            : TodoDocumentMapper.ToDomain(persistedDocument);
    }
}
