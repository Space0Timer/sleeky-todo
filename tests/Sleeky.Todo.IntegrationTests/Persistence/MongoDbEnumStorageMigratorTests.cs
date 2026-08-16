using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.DependencyInjection;
using Sleeky.Todo.Infrastructure.Persistence;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Persistence;

/// <summary>
/// Covers the startup rewrite that turns enum names stored as strings into the
/// integers the documents are mapped with.
/// </summary>
/// <remarks>
/// <para>
/// The migrator is internal to the infrastructure assembly, so the suite starts
/// the registered hosted services the way the host does rather than
/// constructing it.
/// </para>
/// <para>
/// The name a status was stored under is part of the on-disk contract, so
/// renaming a member leaves documents behind that only the old name resolves.
/// That is what the legacy cases here pin: an unrecognized value fails startup
/// outright rather than being skipped, so losing the old name takes the
/// application down with it.
/// </para>
/// </remarks>
[TestClass]
public sealed class MongoDbEnumStorageMigratorTests
{
    private static readonly Guid OwnerId = Id("owner-1");

    private static MongoDbContainer? mongoDbContainer;

    private IMongoCollection<BsonDocument> collection = null!;
    private string databaseName = null!;
    private ServiceProvider? serviceProvider;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext testContext)
    {
        if (!ShouldRunMongoDbTests())
        {
            return;
        }

        mongoDbContainer = new MongoDbBuilder("mongo:8.0").Build();
        await mongoDbContainer.StartAsync(testContext.CancellationToken);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (mongoDbContainer is not null)
        {
            await mongoDbContainer.DisposeAsync();
        }
    }

    [TestInitialize]
    public void TestInitialize()
    {
        if (mongoDbContainer is null)
        {
            Assert.Inconclusive(
                "Set RUN_MONGODB_INTEGRATION_TESTS=true and start Docker to run MongoDB migration tests.");
        }

        databaseName = $"sleekyTodoEnumMigrationTests_{Guid.NewGuid():N}";
        collection = new MongoClient(mongoDbContainer.GetConnectionString())
            .GetDatabase(databaseName)
            .GetCollection<BsonDocument>("todoItems");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        serviceProvider?.Dispose();
        serviceProvider = null;
    }

    /// <summary>
    /// The name <see cref="TodoStatus.Open"/> was stored under before it was
    /// renamed. A document written then still says so, and nothing rewrites it
    /// on read, so the migrator is the only thing standing between that document
    /// and a failed startup.
    /// </summary>
    [TestMethod]
    public async Task AStatusStoredUnderTheNameOpenWasRenamedFromIsMigrated()
    {
        await collection.InsertOneAsync(CreateDocument("legacyOpen", "NotStarted"));

        await StartHostedServicesAsync();

        BsonDocument migrated = await ReadAsync("legacyOpen");
        migrated["status"].AsInt32.Should().Be((int)TodoStatus.Open);
    }

    [TestMethod]
    public async Task StatusesStoredAsCurrentNamesAreMigrated()
    {
        await collection.InsertOneAsync(CreateDocument("named", "InProgress"));

        await StartHostedServicesAsync();

        BsonDocument migrated = await ReadAsync("named");
        migrated["status"].AsInt32.Should().Be((int)TodoStatus.InProgress);
    }

    [TestMethod]
    public async Task StatusesAlreadyStoredAsIntegersAreLeftAlone()
    {
        await collection.InsertOneAsync(CreateDocument(
            "integer",
            new BsonInt32((int)TodoStatus.Completed)));

        await StartHostedServicesAsync();

        BsonDocument migrated = await ReadAsync("integer");
        migrated["status"].AsInt32.Should().Be((int)TodoStatus.Completed);
    }

    /// <summary>
    /// Refusing to start is the deliberate response to a value the migrator
    /// cannot place: rewriting it to a default would silently reassign whatever
    /// the document actually meant.
    /// </summary>
    [TestMethod]
    public async Task AnUnrecognizedStatusFailsStartup()
    {
        await collection.InsertOneAsync(CreateDocument("unknown", "Postponed"));

        Func<Task> start = StartHostedServicesAsync;

        await start.Should().ThrowAsync<InvalidOperationException>()
            .Where(exception => exception.Message.Contains("Postponed", StringComparison.Ordinal));
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static Guid Id(string value)
    {
        byte[] bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }

    /// <summary>
    /// A document carrying the given stored status. Search tokens are present so
    /// the backfill that runs alongside this migrator has nothing to do here.
    /// </summary>
    private static BsonDocument CreateDocument(string id, BsonValue status)
    {
        DateTime timestamp = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        return new BsonDocument
        {
            { "_id", new BsonBinaryData(Id(id), GuidRepresentation.Standard) },
            { "ownerId", new BsonBinaryData(OwnerId, GuidRepresentation.Standard) },
            { "name", "Submit Report" },
            { "nameNormalized", "submit report" },
            { "searchTokens", new BsonArray(new[] { "submit", "report" }) },
            { "description", BsonNull.Value },
            { "dueDate", "2026-08-31" },
            { "status", status },
            { "priority", 1 },
            { "dependencyIds", new BsonArray() },
            { "recurrence", BsonNull.Value },
            { "seriesId", BsonNull.Value },
            { "occurrenceNumber", BsonNull.Value },
            { "version", 1L },
            { "createdAt", timestamp },
            { "updatedAt", timestamp },
            { "deletedAt", BsonNull.Value },
            { "purgeAt", BsonNull.Value },
        };
    }

    private async Task<BsonDocument> ReadAsync(string id)
    {
        return await collection
            .Find(Builders<BsonDocument>.Filter.Eq(
                "_id",
                new BsonBinaryData(Id(id), GuidRepresentation.Standard)))
            .FirstAsync();
    }

    private async Task StartHostedServicesAsync()
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            [$"{MongoDbSettings.SectionName}:ConnectionString"] =
                mongoDbContainer!.GetConnectionString(),
            [$"{MongoDbSettings.SectionName}:DatabaseName"] = databaseName,
            [$"{MongoDbSettings.SectionName}:TodoItemsCollectionName"] = "todoItems",
        };
        ServiceCollection services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new TestCurrentUser(OwnerId));
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build());

        serviceProvider = services.BuildServiceProvider();

        foreach (IHostedService hostedService in
            serviceProvider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }
}
