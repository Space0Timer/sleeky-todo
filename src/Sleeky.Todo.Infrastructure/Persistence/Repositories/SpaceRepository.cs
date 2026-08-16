using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Documents;
using Sleeky.Todo.Infrastructure.Persistence.Transactions;

namespace Sleeky.Todo.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence for <see cref="Space"/>.
/// </summary>
/// <remarks>
/// Deliberately unscoped: the TODO repository reads the ambient Space scope,
/// but this repository is what the access check reads to decide that scope,
/// so it cannot depend on it. Its callers are the access service and the
/// Space handlers, which are themselves guarded by the pipeline.
/// </remarks>
internal sealed class SpaceRepository : ISpaceRepository
{
    private const string ResourceName = "Space";

    private readonly SessionAwareCollection<SpaceDocument> spaces;

    public SpaceRepository(
        IMongoCollection<SpaceDocument> spaces,
        MongoTransactionContext? transactionContext = null)
    {
        ArgumentNullException.ThrowIfNull(spaces);

        this.spaces = new SessionAwareCollection<SpaceDocument>(
            spaces,
            transactionContext ?? new MongoTransactionContext());
    }

    public async Task AddAsync(Space space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        await spaces.InsertOneAsync(SpaceDocumentMapper.FromDomain(space), cancellationToken);
    }

    /// <summary>
    /// Inserts first and reads on collision, rather than reading first and
    /// inserting on absence: the insert is the step the unique <c>_id</c>
    /// makes atomic, so two racers both land here and one of them simply
    /// reads what the other wrote.
    /// </summary>
    public async Task<Space> GetOrAddAsync(
        Space space,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        try
        {
            await spaces.InsertOneAsync(SpaceDocumentMapper.FromDomain(space), cancellationToken);

            return space;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return await ReadExistingAsync(space.Id, cancellationToken);
        }
    }

    public async Task<Space?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        SpaceDocument? document = await spaces
            .Find(BuildIdFilter(id))
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : SpaceDocumentMapper.ToDomain(document);
    }

    public async Task<IReadOnlyCollection<Space>> GetForSubjectAsync(
        Guid subjectId,
        SubjectType subjectType,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<SpaceDocument> filter = Builders<SpaceDocument>.Filter.ElemMatch(
            document => document.Access,
            entry => entry.SubjectId == subjectId && entry.SubjectType == subjectType);
        SortDefinition<SpaceDocument> oldestFirst = Builders<SpaceDocument>.Sort
            .Ascending(document => document.CreatedAt)
            .Ascending(document => document.Id);

        List<SpaceDocument> documents = await spaces
            .Find(filter)
            .Sort(oldestFirst)
            .ToListAsync(cancellationToken);

        return documents.Select(SpaceDocumentMapper.ToDomain).ToArray();
    }

    /// <summary>
    /// Replaces the stored document only while it still carries the version
    /// the Space was loaded at, so a concurrent membership change cannot be
    /// overwritten — the same shape as the TODO repository's versioned write.
    /// </summary>
    public async Task<Space> UpdateAsync(Space space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        long expectedVersion = space.Version;
        FilterDefinition<SpaceDocument> filter = BuildIdFilter(space.Id)
            & Builders<SpaceDocument>.Filter.Eq(document => document.Version, expectedVersion);
        SpaceDocument replacement = SpaceDocumentMapper.FromDomain(
            space,
            checked(expectedVersion + 1));
        FindOneAndReplaceOptions<SpaceDocument> options = new FindOneAndReplaceOptions<SpaceDocument>
        {
            ReturnDocument = ReturnDocument.After,
        };

        SpaceDocument? persisted = await spaces.FindOneAndReplaceAsync(
            filter,
            replacement,
            options,
            cancellationToken);

        return persisted is null
            ? throw new ConcurrencyConflictException(ResourceName, space.Id, expectedVersion)
            : SpaceDocumentMapper.ToDomain(persisted);
    }

    private static FilterDefinition<SpaceDocument> BuildIdFilter(Guid id)
    {
        return Builders<SpaceDocument>.Filter.Eq(document => document.Id, id);
    }

    private async Task<Space> ReadExistingAsync(Guid id, CancellationToken cancellationToken)
    {
        SpaceDocument document = await spaces
            .Find(BuildIdFilter(id))
            .FirstAsync(cancellationToken);

        return SpaceDocumentMapper.ToDomain(document);
    }
}
