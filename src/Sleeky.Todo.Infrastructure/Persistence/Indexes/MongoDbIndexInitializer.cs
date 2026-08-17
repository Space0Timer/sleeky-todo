using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Indexes;

internal sealed class MongoDbIndexInitializer : IHostedService
{
    private const int IndexesInitializedEventId = 2001;
    private const int IndexDroppedEventId = 2002;
    private const int IndexNotFoundErrorCode = 27;
    private const string IndexNameField = "name";
    private const int NamespaceNotFoundErrorCode = 26;

    /// <summary>
    /// Index names replaced by their Space-scoped equivalents: first the
    /// unscoped originals, then the owner-scoped generation that followed
    /// them. Index creation never removes a previous definition, so an
    /// existing deployment would otherwise keep paying write cost for indexes
    /// no query can use.
    /// </summary>
    private static readonly string[] SupersededTodoIndexNames =
    [
        "active_due_date_id",
        "active_priority_id",
        "active_status_id",
        "active_name_normalized_id",
        "active_dependency_ids",
        "unique_series_occurrence",
        "owner_active_due_date_id",
        "owner_active_priority_id",
        "owner_active_status_id",
        "owner_active_name_normalized_id",
        "owner_active_dependency_ids",
        "owner_active_search_tokens",
        "owner_unique_series_occurrence",
    ];

    private readonly ILogger<MongoDbIndexInitializer> logger;
    private readonly IMongoCollection<TodoDocument> todoItems;
    private readonly IMongoCollection<UserDocument> users;

    public MongoDbIndexInitializer(
        IMongoCollection<TodoDocument> todoItems,
        IMongoCollection<UserDocument> users,
        ILogger<MongoDbIndexInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(todoItems);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(logger);

        this.todoItems = todoItems;
        this.users = users;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await DropSupersededTodoIndexesAsync(cancellationToken);

        CreateIndexModel<TodoDocument>[] todoIndexes = BuildTodoIndexes();
        _ = await this.todoItems.Indexes.CreateManyAsync(
            todoIndexes,
            cancellationToken: cancellationToken);

        CreateIndexModel<UserDocument>[] userIndexes = BuildUserIndexes();
        _ = await this.users.Indexes.CreateManyAsync(
            userIndexes,
            cancellationToken: cancellationToken);

        this.logger.LogInformation(
            IndexesInitializedEventId,
            "Initialized {TodoIndexCount} MongoDB TODO indexes and {UserIndexCount} user indexes",
            todoIndexes.Length,
            userIndexes.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static CreateIndexModel<TodoDocument>[] BuildTodoIndexes()
    {
        IndexKeysDefinitionBuilder<TodoDocument> keys =
            Builders<TodoDocument>.IndexKeys;

        return
        [
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.SpaceId)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.DueDate)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "space_active_due_date_id",
                }),
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.SpaceId)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.Priority)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "space_active_priority_id",
                }),
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.SpaceId)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.Status)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "space_active_status_id",
                }),
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.SpaceId)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.NameNormalized)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "space_active_name_normalized_id",
                }),
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.SpaceId)
                    .Ascending(todo => todo.DependencyIds)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.Status),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "space_active_dependency_ids",
                }),

            // The array key comes last here, unlike the dependency index above.
            // A search matches a Space and a scope exactly and then scans a
            // range of tokens, so equality has to precede the range for the
            // bounds to be tight. The dependency lookup matches an exact
            // identifier in the array instead, where the position does not
            // carry the same cost.
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.SpaceId)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.SearchTokens),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = MongoTodoIndexNames.SpaceActiveSearchTokens,
                }),
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.PurgeAt),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "purge_at",
                    PartialFilterExpression = Builders<TodoDocument>.Filter.Type(
                        todo => todo.PurgeAt,
                        BsonType.DateTime),
                }),
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.SpaceId)
                    .Ascending(todo => todo.SeriesId)
                    .Ascending(todo => todo.OccurrenceNumber),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "space_unique_series_occurrence",
                    Unique = true,
                    PartialFilterExpression = Builders<TodoDocument>.Filter.Type(
                        todo => todo.SeriesId,
                        BsonType.Binary)
                        & Builders<TodoDocument>.Filter.Type(
                            todo => todo.OccurrenceNumber,
                            BsonType.Int32),
                }),
        ];
    }

    /// <summary>
    /// The identity mapping, and the two the user search walks.
    /// </summary>
    /// <remarks>
    /// The search matches an anchored prefix against a lower-cased copy of the
    /// name and of the address, which is a range over the index keys. Without
    /// these two the same query reads the whole collection on every keystroke.
    /// Neither is unique: two accounts may share a display name, and a
    /// provider that reports no address leaves both fields absent.
    /// </remarks>
    private static CreateIndexModel<UserDocument>[] BuildUserIndexes()
    {
        IndexKeysDefinitionBuilder<UserDocument> keys = Builders<UserDocument>.IndexKeys;

        return
        [
            new CreateIndexModel<UserDocument>(
                keys.Ascending(user => user.Issuer)
                    .Ascending(user => user.Subject),
                new CreateIndexOptions<UserDocument>
                {
                    Name = "unique_user_issuer_subject",
                    Unique = true,
                }),
            new CreateIndexModel<UserDocument>(
                keys.Ascending(user => user.DisplayNameNormalized),
                new CreateIndexOptions<UserDocument>
                {
                    Name = "user_display_name_normalized",
                }),
            new CreateIndexModel<UserDocument>(
                keys.Ascending(user => user.EmailNormalized),
                new CreateIndexOptions<UserDocument>
                {
                    Name = "user_email_normalized",
                }),
        ];
    }

    private async Task<HashSet<string>> ListTodoIndexNamesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using IAsyncCursor<BsonDocument> cursor = await this.todoItems.Indexes
                .ListAsync(cancellationToken);
            List<BsonDocument> indexes = await cursor.ToListAsync(cancellationToken);

            return indexes
                .Select(index => index
                    .GetValue(IndexNameField, BsonString.Empty)
                    .AsString)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (MongoCommandException exception)
            when (exception.Code == NamespaceNotFoundErrorCode)
        {
            // A collection that does not exist yet carries no superseded index.
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Reads the existing index names once rather than issuing a drop per
    /// superseded name, so a started instance does not pay a failed command for
    /// every index that earlier runs already removed.
    /// </summary>
    private async Task DropSupersededTodoIndexesAsync(
        CancellationToken cancellationToken)
    {
        HashSet<string> existingNames = await ListTodoIndexNamesAsync(
            cancellationToken);

        foreach (string indexName in SupersededTodoIndexNames)
        {
            if (!existingNames.Contains(indexName))
            {
                continue;
            }

            try
            {
                await this.todoItems.Indexes.DropOneAsync(
                    indexName,
                    cancellationToken);
                this.logger.LogInformation(
                    IndexDroppedEventId,
                    "Dropped superseded MongoDB TODO index {IndexName}",
                    indexName);
            }
            catch (MongoCommandException exception)
                when (exception.Code == IndexNotFoundErrorCode)
            {
                // A concurrently starting instance removed it first.
            }
        }
    }
}
