using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Domain.Services;
using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Migrations;

/// <summary>
/// Gives documents written before search existed the tokens the search index
/// is built over.
/// </summary>
/// <remarks>
/// Tokens come from the same <see cref="SearchTokenizer"/> the write path uses,
/// so a backfilled document is indistinguishable from one the application has
/// since rewritten.
///
/// Documents are read as raw BSON rather than through
/// <see cref="TodoDocument"/>, because that mapping now supplies an empty token
/// list for a document that has none, and an empty list is exactly what this
/// has to tell apart from a missing field.
/// </remarks>
internal sealed class MongoDbSearchTokensMigrator : IHostedService
{
    private const int SearchTokensBackfilledEventId = 2012;

    /// <summary>
    /// Batch size for the bulk write. Each document needs its own tokens, so
    /// this cannot collapse into one update the way the enum migration does.
    /// </summary>
    private const int BatchSize = 500;

    private readonly IMongoCollection<BsonDocument> collection;
    private readonly ILogger<MongoDbSearchTokensMigrator> logger;

    public MongoDbSearchTokensMigrator(
        IMongoCollection<TodoDocument> todoItems,
        ILogger<MongoDbSearchTokensMigrator> logger)
    {
        ArgumentNullException.ThrowIfNull(todoItems);
        ArgumentNullException.ThrowIfNull(logger);

        this.collection = todoItems.Database.GetCollection<BsonDocument>(
            todoItems.CollectionNamespace.CollectionName);
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        long backfilled = await this.BackfillAsync(cancellationToken);

        if (backfilled > 0)
        {
            this.logger.LogInformation(
                SearchTokensBackfilledEventId,
                "Backfilled MongoDB TODO search tokens for {DocumentCount} documents",
                backfilled);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static UpdateOneModel<BsonDocument> BuildTokenWrite(BsonDocument document)
    {
        IReadOnlyList<string> tokens = SearchTokenizer.Tokenize(
            ReadText(document, MongoTodoFields.Name),
            ReadText(document, MongoTodoFields.Description));

        return new UpdateOneModel<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq(
                MongoTodoFields.Id,
                document[MongoTodoFields.Id]),
            Builders<BsonDocument>.Update.Set(
                MongoTodoFields.SearchTokens,
                new BsonArray(tokens)));
    }

    private static string? ReadText(BsonDocument document, string field)
    {
        return document.TryGetValue(field, out BsonValue? value) && value.IsString
            ? value.AsString
            : null;
    }

    /// <summary>
    /// The filter is unindexed, so every start pays one collection scan even
    /// when nothing needs backfilling. That matches what the enum migration
    /// beside this already costs, and adding an index to make a one-off
    /// migration cheap would outlive the migration itself.
    /// </summary>
    private async Task<long> BackfillAsync(CancellationToken cancellationToken)
    {
        FilterDefinition<BsonDocument> missingTokens = Builders<BsonDocument>.Filter.Exists(
            MongoTodoFields.SearchTokens,
            false);
        FindOptions<BsonDocument> options = new FindOptions<BsonDocument>
        {
            BatchSize = BatchSize,
            Projection = Builders<BsonDocument>.Projection
                .Include(MongoTodoFields.Name)
                .Include(MongoTodoFields.Description),
        };

        long backfilled = 0;
        List<UpdateOneModel<BsonDocument>> writes =
            new List<UpdateOneModel<BsonDocument>>(BatchSize);

        using IAsyncCursor<BsonDocument> cursor = await this.collection.FindAsync(
            missingTokens,
            options,
            cancellationToken);

        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (BsonDocument document in cursor.Current)
            {
                writes.Add(BuildTokenWrite(document));

                if (writes.Count < BatchSize)
                {
                    continue;
                }

                backfilled += await this.WriteBatchAsync(writes, cancellationToken);
            }
        }

        backfilled += await this.WriteBatchAsync(writes, cancellationToken);

        return backfilled;
    }

    private async Task<long> WriteBatchAsync(
        List<UpdateOneModel<BsonDocument>> writes,
        CancellationToken cancellationToken)
    {
        if (writes.Count == 0)
        {
            return 0;
        }

        BulkWriteResult<BsonDocument> result = await this.collection.BulkWriteAsync(
            writes,
            new BulkWriteOptions { IsOrdered = false },
            cancellationToken);
        writes.Clear();

        return result.ModifiedCount;
    }
}
