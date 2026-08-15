using MongoDB.Driver;

using Sleeky.Todo.Infrastructure.Persistence.Transactions;

namespace Sleeky.Todo.Infrastructure.Persistence.Repositories;

/// <summary>
/// Routes a collection call through the ambient transaction when one is open,
/// and straight at the collection when none is.
/// </summary>
/// <remarks>
/// The driver models the two as separate overloads — one taking an
/// <see cref="IClientSessionHandle"/> and one not — so without this every call
/// site repeats the same null check, and a site that forgets it silently
/// escapes the transaction it was meant to join. Extension methods cannot close
/// the gap: an extension taking a nullable handle loses overload resolution to
/// the driver's own instance method, because nullability is erased at runtime.
/// A named type has no such ambiguity.
/// </remarks>
/// <typeparam name="TDocument">The stored document type.</typeparam>
internal sealed class SessionAwareCollection<TDocument>
{
    private readonly IMongoCollection<TDocument> collection;
    private readonly MongoTransactionContext transactionContext;

    public SessionAwareCollection(
        IMongoCollection<TDocument> collection,
        MongoTransactionContext transactionContext)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(transactionContext);

        this.collection = collection;
        this.transactionContext = transactionContext;
    }

    private IClientSessionHandle? Session => transactionContext.Session;

    public Task InsertOneAsync(
        TDocument document,
        CancellationToken cancellationToken)
    {
        return Session is null
            ? collection.InsertOneAsync(
                document,
                cancellationToken: cancellationToken)
            : collection.InsertOneAsync(
                Session,
                document,
                cancellationToken: cancellationToken);
    }

    public IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter)
    {
        return Session is null
            ? collection.Find(filter)
            : collection.Find(Session, filter);
    }

    public Task<long> CountDocumentsAsync(
        FilterDefinition<TDocument> filter,
        CountOptions options,
        CancellationToken cancellationToken)
    {
        return Session is null
            ? collection.CountDocumentsAsync(filter, options, cancellationToken)
            : collection.CountDocumentsAsync(Session, filter, options, cancellationToken);
    }

    public Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
        FilterDefinition<TDocument> filter,
        FindOptions<TDocument, TProjection> options,
        CancellationToken cancellationToken)
    {
        return Session is null
            ? collection.FindAsync(filter, options, cancellationToken)
            : collection.FindAsync(Session, filter, options, cancellationToken);
    }

    public Task<BulkWriteResult<TDocument>> BulkWriteAsync(
        IEnumerable<WriteModel<TDocument>> writes,
        BulkWriteOptions options,
        CancellationToken cancellationToken)
    {
        return Session is null
            ? collection.BulkWriteAsync(writes, options, cancellationToken)
            : collection.BulkWriteAsync(Session, writes, options, cancellationToken);
    }

    public Task<TDocument> FindOneAndReplaceAsync(
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        FindOneAndReplaceOptions<TDocument> options,
        CancellationToken cancellationToken)
    {
        return Session is null
            ? collection.FindOneAndReplaceAsync(
                filter,
                replacement,
                options,
                cancellationToken)
            : collection.FindOneAndReplaceAsync(
                Session,
                filter,
                replacement,
                options,
                cancellationToken);
    }
}
