using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Infrastructure.Persistence.Documents;
using Sleeky.Todo.Infrastructure.Persistence.Transactions;

namespace Sleeky.Todo.Infrastructure.Persistence.Repositories;

internal sealed class MongoUserDirectoryRepository : IUserDirectoryRepository
{
    private const int DuplicateKeyErrorCode = 11000;

    private readonly IClock clock;
    private readonly SessionAwareCollection<UserDocument> users;

    public MongoUserDirectoryRepository(
        IMongoCollection<UserDocument> users,
        IClock clock,
        MongoTransactionContext? transactionContext = null)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(clock);

        this.users = new SessionAwareCollection<UserDocument>(
            users,
            transactionContext ?? new MongoTransactionContext());
        this.clock = clock;
    }

    public async Task<UserIdentity> ResolveAsync(
        string issuer,
        string subject,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        FilterDefinition<UserDocument> filter = BuildIdentityFilter(issuer, subject);
        UpdateDefinition<UserDocument> update = BuildResolveUpdate(
            issuer,
            subject,
            displayName,
            clock.UtcNow.UtcDateTime);
        FindOneAndUpdateOptions<UserDocument> options =
            new FindOneAndUpdateOptions<UserDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After,
            };

        try
        {
            UserDocument? document = await users.FindOneAndUpdateAsync(
                filter,
                update,
                options,
                cancellationToken);

            return ToIdentity(document
                ?? throw new InvalidOperationException(
                    "An upserting resolve must return a user document."));
        }
        catch (MongoCommandException exception)
            when (exception.Code == DuplicateKeyErrorCode)
        {
            return await ReadExistingAsync(filter, cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return await ReadExistingAsync(filter, cancellationToken);
        }
    }

    private static FilterDefinition<UserDocument> BuildIdentityFilter(
        string issuer,
        string subject)
    {
        return Builders<UserDocument>.Filter.Eq(user => user.Issuer, issuer)
            & Builders<UserDocument>.Filter.Eq(user => user.Subject, subject);
    }

    /// <summary>
    /// Writes the display name only when the provider supplied one.
    /// </summary>
    /// <remarks>
    /// The name is an optional claim: a response carrying neither
    /// <c>name</c> nor <c>preferred_username</c> resolves it to null, and
    /// setting that unconditionally would erase a name an earlier login had
    /// stored. <c>SetOnInsert</c> would not do either — it would pin the name
    /// at first login and never track a rename at the provider — so the write
    /// is kept and made conditional instead.
    /// </remarks>
    private static UpdateDefinition<UserDocument> BuildResolveUpdate(
        string issuer,
        string subject,
        string? displayName,
        DateTime timestamp)
    {
        UpdateDefinitionBuilder<UserDocument> updates = Builders<UserDocument>.Update;
        List<UpdateDefinition<UserDocument>> definitions =
        [
            updates.SetOnInsert(user => user.Id, Guid.NewGuid()),
            updates.SetOnInsert(user => user.CreatedAt, timestamp),
            updates.SetOnInsert(user => user.Issuer, issuer),
            updates.SetOnInsert(user => user.Subject, subject),
            updates.Set(user => user.LastLoginAt, timestamp),
        ];

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            definitions.Add(updates.Set(user => user.DisplayName, displayName));
        }

        return updates.Combine(definitions);
    }

    private static UserIdentity ToIdentity(UserDocument document)
    {
        return new UserIdentity(document.Id, document.DisplayName);
    }

    private async Task<UserIdentity> ReadExistingAsync(
        FilterDefinition<UserDocument> filter,
        CancellationToken cancellationToken)
    {
        UserDocument document = await users
            .Find(filter)
            .FirstAsync(cancellationToken);

        return ToIdentity(document);
    }
}
