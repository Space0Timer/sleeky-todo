using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MongoDB.Driver;

using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Indexes;

/// <summary>
/// Creates the Space collection's indexes at startup.
/// </summary>
/// <remarks>
/// A hosted service of its own rather than a section of
/// <see cref="MongoDbIndexInitializer"/>, so the Space collection's indexes
/// evolve without touching the TODO collection's; both are idempotent and
/// order-independent, so either may run first.
/// </remarks>
internal sealed class MongoSpaceIndexInitializer : IHostedService
{
    private const int IndexesInitializedEventId = 2003;

    /// <summary>
    /// The membership lookup: every Space a subject appears in. Multikey over
    /// the embedded access list, keyed by identifier then type, matching the
    /// order the membership filter names them.
    /// </summary>
    private const string AccessSubjectIndexName = "access_subject";

    private readonly ILogger<MongoSpaceIndexInitializer> logger;
    private readonly IMongoCollection<SpaceDocument> spaces;

    public MongoSpaceIndexInitializer(
        IMongoCollection<SpaceDocument> spaces,
        ILogger<MongoSpaceIndexInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(spaces);
        ArgumentNullException.ThrowIfNull(logger);

        this.spaces = spaces;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        CreateIndexModel<SpaceDocument>[] indexes = BuildIndexes();
        _ = await this.spaces.Indexes.CreateManyAsync(
            indexes,
            cancellationToken: cancellationToken);

        this.logger.LogInformation(
            IndexesInitializedEventId,
            "Initialized {SpaceIndexCount} MongoDB Space indexes",
            indexes.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static CreateIndexModel<SpaceDocument>[] BuildIndexes()
    {
        IndexKeysDefinitionBuilder<SpaceDocument> keys = Builders<SpaceDocument>.IndexKeys;

        return
        [
            new CreateIndexModel<SpaceDocument>(
                keys.Ascending(MongoSpaceFields.AccessSubjectId)
                    .Ascending(MongoSpaceFields.AccessSubjectType),
                new CreateIndexOptions
                {
                    Name = AccessSubjectIndexName,
                }),
        ];
    }
}
