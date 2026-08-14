using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Infrastructure.DependencyInjection;
using Sleeky.Todo.Infrastructure.Persistence;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Persistence;

/// <summary>
/// Covers the startup backfill that gives documents written before search
/// existed the tokens the search index is built over.
/// </summary>
/// <remarks>
/// The migrator is internal to the infrastructure assembly, so the suite starts
/// the registered hosted services the way the host does rather than
/// constructing it. That also exercises the registration order the backfill
/// depends on: tokens are written before the index covering them is built.
/// </remarks>
[TestClass]
public sealed class MongoDbSearchTokensMigratorTests
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

        mongoDbContainer = new MongoDbBuilder("mongo:7.0").Build();
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

        databaseName = $"sleekyTodoMigrationTests_{Guid.NewGuid():N}";
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

    [TestMethod]
    public async Task DocumentsWithoutTokensAreBackfilledFromTheirNameAndDescription()
    {
        await collection.InsertOneAsync(CreateLegacyDocument(
            "legacy",
            "Submit Quarterly Report",
            "Include the VAT summary"));

        await StartHostedServicesAsync();

        BsonDocument migrated = await ReadAsync("legacy");
        ReadTokens(migrated).Should().Equal(
            "submit",
            "quarterly",
            "report",
            "include",
            "the",
            "vat",
            "summary");
    }

    [TestMethod]
    public async Task ADocumentWithoutADescriptionIsTokenizedFromItsNameAlone()
    {
        await collection.InsertOneAsync(CreateLegacyDocument("nameOnly", "Renew Passport"));

        await StartHostedServicesAsync();

        ReadTokens(await ReadAsync("nameOnly")).Should().Equal("renew", "passport");
    }

    /// <summary>
    /// The filter selects a missing field rather than an empty array, so a
    /// document the application already wrote keeps the tokens it was written
    /// with. The stored value here is deliberately not what the tokenizer would
    /// produce, which is what makes an unwanted rewrite visible.
    /// </summary>
    [TestMethod]
    public async Task DocumentsThatAlreadyCarryTokensAreLeftAlone()
    {
        BsonDocument document = CreateLegacyDocument("tokened", "Submit Report");
        document["searchTokens"] = new BsonArray(new[] { "sentinel" });
        await collection.InsertOneAsync(document);

        await StartHostedServicesAsync();

        ReadTokens(await ReadAsync("tokened")).Should().Equal("sentinel");
    }

    [TestMethod]
    public async Task ASecondRunChangesNothing()
    {
        await collection.InsertOneAsync(CreateLegacyDocument("repeat", "Submit Report"));

        await StartHostedServicesAsync();
        BsonDocument afterFirstRun = await ReadAsync("repeat");

        TestCleanup();
        await StartHostedServicesAsync();
        BsonDocument afterSecondRun = await ReadAsync("repeat");

        ReadTokens(afterFirstRun).Should().Equal("submit", "report");
        afterSecondRun.Should().BeEquivalentTo(afterFirstRun);
    }

    /// <summary>
    /// A batch is written per five hundred documents, so the loop is measured
    /// past that boundary rather than only within one batch.
    /// </summary>
    [TestMethod]
    public async Task BackfillSpansMoreDocumentsThanOneBatchHolds()
    {
        const int documentCount = 1201;
        await collection.InsertManyAsync(Enumerable
            .Range(0, documentCount)
            .Select(index => CreateLegacyDocument($"bulk-{index}", $"Task {index}")));

        await StartHostedServicesAsync();

        long remaining = await collection.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Exists("searchTokens", false));
        remaining.Should().Be(0);
        ReadTokens(await ReadAsync("bulk-1200")).Should().Equal("task", "1200");
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

    private static string[] ReadTokens(BsonDocument document)
    {
        return document["searchTokens"].AsBsonArray
            .Select(token => token.AsString)
            .ToArray();
    }

    /// <summary>
    /// A document as it was stored before this feature: every field the
    /// application writes except the tokens.
    /// </summary>
    private static BsonDocument CreateLegacyDocument(
        string id,
        string name,
        string? description = null)
    {
        DateTime timestamp = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        return new BsonDocument
        {
            { "_id", new BsonBinaryData(Id(id), GuidRepresentation.Standard) },
            { "ownerId", new BsonBinaryData(OwnerId, GuidRepresentation.Standard) },
            { "name", name },
            { "nameNormalized", name.ToLowerInvariant() },
            {
                "description",
                description is null ? BsonNull.Value : new BsonString(description)
            },
            { "dueDate", "2026-08-31" },
            { "status", 0 },
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

        // The hosted services log their progress, which the reader and
        // repository suites never resolve.
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
