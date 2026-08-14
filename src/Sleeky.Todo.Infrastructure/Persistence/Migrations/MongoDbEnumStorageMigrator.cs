using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Migrations;

internal sealed class MongoDbEnumStorageMigrator : IHostedService
{
    private static readonly IReadOnlyDictionary<string, int> StatusValues =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(TodoStatus.NotStarted)] = (int)TodoStatus.NotStarted,
            [nameof(TodoStatus.InProgress)] = (int)TodoStatus.InProgress,
            [nameof(TodoStatus.Completed)] = (int)TodoStatus.Completed,
            [nameof(TodoStatus.Archived)] = (int)TodoStatus.Archived,
        };

    private static readonly IReadOnlyDictionary<string, int> PriorityValues =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(TodoPriority.Low)] = (int)TodoPriority.Low,
            [nameof(TodoPriority.Medium)] = (int)TodoPriority.Medium,
            [nameof(TodoPriority.High)] = (int)TodoPriority.High,
        };

    private readonly IMongoCollection<BsonDocument> collection;
    private readonly ILogger<MongoDbEnumStorageMigrator> logger;

    /// <summary>
    /// Rewrites stored enum representations, so it reads the TODO collection as
    /// raw BSON rather than through <see cref="TodoDocument"/>, whose mapping is
    /// exactly what the migration is repairing.
    /// </summary>
    public MongoDbEnumStorageMigrator(
        IMongoCollection<TodoDocument> todoItems,
        ILogger<MongoDbEnumStorageMigrator> logger)
    {
        ArgumentNullException.ThrowIfNull(todoItems);
        ArgumentNullException.ThrowIfNull(logger);

        this.collection = todoItems.Database.GetCollection<BsonDocument>(
            todoItems.CollectionNamespace.CollectionName);
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ValidateStoredValuesAsync(
            MongoTodoFields.Status,
            StatusValues,
            cancellationToken);
        await ValidateStoredValuesAsync(
            MongoTodoFields.Priority,
            PriorityValues,
            cancellationToken);

        long migratedStatuses = await MigrateFieldAsync(
            MongoTodoFields.Status,
            StatusValues,
            cancellationToken);
        long migratedPriorities = await MigrateFieldAsync(
            MongoTodoFields.Priority,
            PriorityValues,
            cancellationToken);

        if (migratedStatuses > 0 || migratedPriorities > 0)
        {
            this.logger.LogInformation(
                2002,
                "Migrated MongoDB TODO enum storage to integers: {StatusCount} statuses and {PriorityCount} priorities",
                migratedStatuses,
                migratedPriorities);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static bool IsSupported(
        BsonValue value,
        IReadOnlyDictionary<string, int> supportedValues)
    {
        if (value.IsString)
        {
            return supportedValues.ContainsKey(value.AsString);
        }

        if (value.IsInt32)
        {
            return supportedValues.Values.Contains(value.AsInt32);
        }

        return false;
    }

    private async Task ValidateStoredValuesAsync(
        string field,
        IReadOnlyDictionary<string, int> supportedValues,
        CancellationToken cancellationToken)
    {
        using IAsyncCursor<BsonValue> cursor = await this.collection.DistinctAsync<BsonValue>(
            field,
            Builders<BsonDocument>.Filter.Empty,
            cancellationToken: cancellationToken);
        List<BsonValue> existingValues = await cursor.ToListAsync(cancellationToken);
        string[] unsupportedValues = existingValues
            .Where(value => !IsSupported(value, supportedValues))
            .Select(value => value.ToJson())
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (unsupportedValues.Length > 0)
        {
            throw new InvalidOperationException(
                $"Cannot migrate MongoDB TODO field '{field}' because it contains unsupported values: {string.Join(", ", unsupportedValues)}.");
        }
    }

    private async Task<long> MigrateFieldAsync(
        string field,
        IReadOnlyDictionary<string, int> values,
        CancellationToken cancellationToken)
    {
        long modifiedCount = 0;
        foreach ((string name, int value) in values)
        {
            UpdateResult result = await this.collection.UpdateManyAsync(
                Builders<BsonDocument>.Filter.Eq(field, name),
                Builders<BsonDocument>.Update.Set(field, value),
                cancellationToken: cancellationToken);
            modifiedCount += result.ModifiedCount;
        }

        return modifiedCount;
    }
}
