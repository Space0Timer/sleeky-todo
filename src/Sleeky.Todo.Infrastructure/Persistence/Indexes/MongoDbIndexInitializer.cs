using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Indexes;

internal sealed class MongoDbIndexInitializer : IHostedService
{
    private readonly IMongoCollection<TodoDocument> collection;

    public MongoDbIndexInitializer(
        IMongoDatabase database,
        IOptions<MongoDbSettings> options)
    {
        collection = database.GetCollection<TodoDocument>(
            options.Value.TodoItemsCollectionName);
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
                    PartialFilterExpression = new BsonDocument(
                        "purgeAt",
                        new BsonDocument("$type", "date")),
                }),
            new CreateIndexModel<TodoDocument>(
                Builders<TodoDocument>.IndexKeys
                    .Ascending(todo => todo.SeriesId)
                    .Ascending(todo => todo.OccurrenceNumber),
                new CreateIndexOptions<TodoDocument>
                {
                    Name = "unique_series_occurrence",
                    Unique = true,
                    PartialFilterExpression = new BsonDocument
                    {
                        { "seriesId", new BsonDocument("$type", "string") },
                        { "occurrenceNumber", new BsonDocument("$type", "int") },
                    },
                }),
        ];

        _ = await collection.Indexes.CreateManyAsync(
            indexes,
            cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
