using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Indexes;

internal sealed class MongoDbIndexInitializer : IHostedService
{
    private const int IndexNotFoundErrorCode = 27;
    private const string IndexNameField = "name";
    private const int NamespaceNotFoundErrorCode = 26;

    /// <summary>
    /// Index names replaced by their owner-scoped equivalents. Index creation
    /// never removes a previous definition, so an existing deployment would
    /// otherwise keep paying write cost for indexes no query can use.
    /// </summary>
    private static readonly string[] SupersededTodoIndexNames =
    [
        "active_due_date_id",
        "active_priority_id",
        "active_status_id",
        "active_name_normalized_id",
        "active_dependency_ids",
        "unique_series_occurrence",
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
            2001,
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
                keys.Ascending(todo => todo.OwnerId)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.DueDate)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "owner_active_due_date_id",
                }),
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.OwnerId)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.Priority)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "owner_active_priority_id",
                }),
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.OwnerId)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.Status)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "owner_active_status_id",
                }),
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.OwnerId)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.NameNormalized)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "owner_active_name_normalized_id",
                }),
            new CreateIndexModel<TodoDocument>(
                keys.Ascending(todo => todo.OwnerId)
                    .Ascending(todo => todo.DependencyIds)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.Status),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "owner_active_dependency_ids",
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
                keys.Ascending(todo => todo.OwnerId)
                    .Ascending(todo => todo.SeriesId)
                    .Ascending(todo => todo.OccurrenceNumber),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "owner_unique_series_occurrence",
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

    private static CreateIndexModel<UserDocument>[] BuildUserIndexes()
    {
        return
        [
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys
                    .Ascending(user => user.Issuer)
                    .Ascending(user => user.Subject),
                new CreateIndexOptions<UserDocument>
                {
                    Name = "unique_user_issuer_subject",
                    Unique = true,
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
                    2002,
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
