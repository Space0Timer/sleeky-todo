using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Repositories;

internal sealed class MongoUserDirectoryRepository : IUserDirectoryRepository
{
    private const int DuplicateKeyErrorCode = 11000;

    private readonly IClock clock;
    private readonly IMongoCollection<UserDocument> users;

    public MongoUserDirectoryRepository(
        IMongoCollection<UserDocument> users,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(clock);

        this.users = users;
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
            UserDocument document = await users.FindOneAndUpdateAsync(
                filter,
                update,
                options,
                cancellationToken);

            return ToIdentity(document);
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

    private static UpdateDefinition<UserDocument> BuildResolveUpdate(
        string issuer,
        string subject,
        string? displayName,
        DateTime timestamp)
    {
        UpdateDefinitionBuilder<UserDocument> updates = Builders<UserDocument>.Update;

        return updates.Combine(
            updates.SetOnInsert(user => user.Id, Guid.NewGuid()),
            updates.SetOnInsert(user => user.CreatedAt, timestamp),
            updates.SetOnInsert(user => user.Issuer, issuer),
            updates.SetOnInsert(user => user.Subject, subject),
            updates.Set(user => user.DisplayName, displayName),
            updates.Set(user => user.LastLoginAt, timestamp));
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
