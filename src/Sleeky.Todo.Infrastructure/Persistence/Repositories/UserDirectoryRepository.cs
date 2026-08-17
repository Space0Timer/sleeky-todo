using System.Text.RegularExpressions;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Repositories;

internal sealed class UserDirectoryRepository : IUserDirectoryRepository
{
    private const int DuplicateKeyErrorCode = 11000;

    /// <summary>
    /// The fields a <see cref="UserIdentity"/> is built from. An include
    /// projection typed back to the document: <see cref="UserDocument"/>
    /// ignores extra elements and every property carries a default, so the
    /// fields left out deserialise harmlessly and are never read.
    /// </summary>
    private static readonly ProjectionDefinition<UserDocument, UserDocument> IdentityFields =
        Builders<UserDocument>.Projection
            .Include(user => user.Id)
            .Include(user => user.DisplayName);

    /// <summary>
    /// What a search answers with: the identity fields plus the address, which
    /// is how someone recognises the right person among similar names.
    /// </summary>
    private static readonly ProjectionDefinition<UserDocument, UserDocument> SearchFields =
        Builders<UserDocument>.Projection
            .Include(user => user.Id)
            .Include(user => user.DisplayName)
            .Include(user => user.Email);

    private readonly IClock clock;
    private readonly IMongoCollection<UserDocument> users;

    public UserDirectoryRepository(
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
        string? email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        FilterDefinition<UserDocument> filter = BuildIdentityFilter(issuer, subject);
        UpdateDefinition<UserDocument> update = BuildResolveUpdate(
            issuer,
            subject,
            displayName,
            email,
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

    public async Task<IReadOnlyCollection<UserIdentity>> FindByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return Array.Empty<UserIdentity>();
        }

        FilterDefinition<UserDocument> filter = Builders<UserDocument>.Filter.In(
            user => user.Id,
            userIds);
        List<UserDocument> documents = await users
            .Find(filter)
            .Project(IdentityFields)
            .ToListAsync(cancellationToken);

        return documents.Select(ToIdentity).ToArray();
    }

    public async Task<IReadOnlyCollection<UserSearchMatch>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        string prefix = Normalize(query) ?? string.Empty;

        if (prefix.Length == 0)
        {
            return Array.Empty<UserSearchMatch>();
        }

        List<UserDocument> documents = await users
            .Find(BuildPrefixFilter(prefix))
            .Project(SearchFields)
            .Sort(Builders<UserDocument>.Sort.Ascending(user => user.DisplayNameNormalized))
            .Limit(limit)
            .ToListAsync(cancellationToken);

        return documents.Select(ToSearchMatch).ToArray();
    }

    private static FilterDefinition<UserDocument> BuildIdentityFilter(
        string issuer,
        string subject)
    {
        return Builders<UserDocument>.Filter.Eq(user => user.Issuer, issuer)
            & Builders<UserDocument>.Filter.Eq(user => user.Subject, subject);
    }

    /// <summary>
    /// A name-or-address prefix, matched against the normalised copies.
    /// </summary>
    /// <remarks>
    /// Anchored regular expressions, which MongoDB answers from an index by
    /// walking the key range the prefix bounds — the same work an equality
    /// match does. The unanchored form has no such range and would read every
    /// document, which is the difference between a typeahead and a table scan.
    /// The prefix is escaped, so a user typing <c>.*</c> searches for those
    /// two characters rather than for everybody.
    /// </remarks>
    private static FilterDefinition<UserDocument> BuildPrefixFilter(string prefix)
    {
        BsonRegularExpression pattern = new BsonRegularExpression(
            $"^{Regex.Escape(prefix)}");
        FilterDefinitionBuilder<UserDocument> filters = Builders<UserDocument>.Filter;

        return filters.Or(
            filters.Regex(user => user.DisplayNameNormalized, pattern),
            filters.Regex(user => user.EmailNormalized, pattern));
    }

    /// <summary>
    /// Writes the display name and the e-mail address only when the provider
    /// supplied them.
    /// </summary>
    /// <remarks>
    /// Both are optional claims: a response carrying neither <c>name</c> nor
    /// <c>preferred_username</c> resolves the name to null, and one without
    /// <c>email</c> resolves the address to null. Setting either
    /// unconditionally would erase what an earlier login had stored.
    /// <c>SetOnInsert</c> would not do either — it would pin the value at first
    /// login and never track a change at the provider — so the writes are kept
    /// and made conditional instead. The normalised copy is written with its
    /// original, never on its own, so the pair cannot drift apart.
    /// </remarks>
    private static UpdateDefinition<UserDocument> BuildResolveUpdate(
        string issuer,
        string subject,
        string? displayName,
        string? email,
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
            definitions.Add(
                updates.Set(user => user.DisplayNameNormalized, Normalize(displayName)));
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            definitions.Add(updates.Set(user => user.Email, email));
            definitions.Add(updates.Set(user => user.EmailNormalized, Normalize(email)));
        }

        return updates.Combine(definitions);
    }

    private static string? Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant();
    }

    private static UserIdentity ToIdentity(UserDocument document)
    {
        return new UserIdentity(document.Id, document.DisplayName);
    }

    private static UserSearchMatch ToSearchMatch(UserDocument document)
    {
        return new UserSearchMatch(document.Id, document.DisplayName, document.Email);
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
