using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Indexes;

internal sealed class MongoDbIndexInitializer : IHostedService
{
    private readonly IMongoCollection<TodoDocument> collection;
    private readonly ILogger<MongoDbIndexInitializer> logger;

    public MongoDbIndexInitializer(
        IMongoDatabase database,
        IOptions<MongoDbSettings> options,
        ILogger<MongoDbIndexInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.collection = database.GetCollection<TodoDocument>(
            options.Value.TodoItemsCollectionName);
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        CreateIndexModel<TodoDocument>[] indexes =
        [
            new CreateIndexModel<TodoDocument>(
                Builders<TodoDocument>.IndexKeys
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.DueDate)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "active_due_date_id",
                }),
            new CreateIndexModel<TodoDocument>(
                Builders<TodoDocument>.IndexKeys
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.Priority)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "active_priority_id",
                }),
            new CreateIndexModel<TodoDocument>(
                Builders<TodoDocument>.IndexKeys
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.Status)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "active_status_id",
                }),
            new CreateIndexModel<TodoDocument>(
                Builders<TodoDocument>.IndexKeys
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.NameNormalized)
                    .Ascending(todo => todo.Id),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "active_name_normalized_id",
                }),
            new CreateIndexModel<TodoDocument>(
                Builders<TodoDocument>.IndexKeys
                    .Ascending(todo => todo.DependencyIds)
                    .Ascending(todo => todo.DeletedAt)
                    .Ascending(todo => todo.Status),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "active_dependency_ids",
                }),
            new CreateIndexModel<TodoDocument>(
                Builders<TodoDocument>.IndexKeys.Ascending(todo => todo.PurgeAt),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "purge_at",
                    PartialFilterExpression = Builders<TodoDocument>.Filter.Type(
                        todo => todo.PurgeAt,
                        BsonType.DateTime),
                }),
            new CreateIndexModel<TodoDocument>(
                Builders<TodoDocument>.IndexKeys
                    .Ascending(todo => todo.SeriesId)
                    .Ascending(todo => todo.OccurrenceNumber),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "unique_series_occurrence",
                    Unique = true,
                    PartialFilterExpression = Builders<TodoDocument>.Filter.Type(
                        todo => todo.SeriesId,
                        BsonType.Binary)
                        & Builders<TodoDocument>.Filter.Type(
                            todo => todo.OccurrenceNumber,
                            BsonType.Int32),
                }),
        ];

        _ = await this.collection.Indexes.CreateManyAsync(
            indexes,
            cancellationToken: cancellationToken);
        this.logger.LogInformation(
            2001,
            "Initialized {IndexCount} MongoDB TODO indexes",
            indexes.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
