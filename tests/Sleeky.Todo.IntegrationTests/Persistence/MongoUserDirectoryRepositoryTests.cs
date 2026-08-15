using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Infrastructure.DependencyInjection;
using Sleeky.Todo.Infrastructure.Persistence;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Persistence;

[TestClass]
public sealed class MongoUserDirectoryRepositoryTests
{
    private const string Issuer = "https://issuer.test/realms/sleeky";
    private const string Subject = "subject-1";

    private static MongoDbContainer? mongoDbContainer;

    private readonly List<ServiceProvider> providers = new List<ServiceProvider>();

    private IMongoDatabase database = null!;
    private IUserDirectoryRepository repository = null!;

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

    /// <summary>
    /// Starts the hosted services so the unique issuer/subject index exists.
    /// </summary>
    /// <remarks>
    /// Without the index the upsert can never collide, and the duplicate-key
    /// fallback in <c>ResolveAsync</c> — the thing that makes two simultaneous
    /// first logins converge on one identity — is unreachable from these tests.
    /// </remarks>
    [TestInitialize]
    public async Task TestInitialize()
    {
        if (mongoDbContainer is null)
        {
            Assert.Inconclusive(
                "Set RUN_MONGODB_INTEGRATION_TESTS=true and start Docker to run MongoDB user directory tests.");
        }

        MongoClient client = new MongoClient(mongoDbContainer.GetConnectionString());
        string databaseName = $"sleekyTodoTests_{Guid.NewGuid():N}";
        database = client.GetDatabase(databaseName);
        repository = CreateRepository(databaseName);

        foreach (IHostedService hostedService in
            providers[^1].GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    [TestCleanup]
    public void TestCleanup()
    {
        foreach (ServiceProvider provider in providers)
        {
            provider.Dispose();
        }

        providers.Clear();
    }

    /// <summary>
    /// A login whose token carries no name claim leaves the stored name alone.
    /// </summary>
    /// <remarks>
    /// The name is optional, and a provider that omits it on one login is not
    /// telling us the user no longer has one. Writing the null through would
    /// erase what an earlier login stored, and the user would watch their own
    /// name disappear from the header for no action they took.
    /// </remarks>
    [TestMethod]
    public async Task ALoginWithoutANameClaimKeepsTheStoredName()
    {
        UserIdentity first = await repository.ResolveAsync(Issuer, Subject, "Ada Lovelace");

        UserIdentity second = await repository.ResolveAsync(Issuer, Subject, null);

        second.UserId.Should().Be(first.UserId);
        second.DisplayName.Should().Be("Ada Lovelace");
        (await ReadStoredNameAsync()).Should().Be("Ada Lovelace");
    }

    [TestMethod]
    public async Task ALoginWithABlankNameClaimKeepsTheStoredName()
    {
        _ = await repository.ResolveAsync(Issuer, Subject, "Ada Lovelace");

        UserIdentity resolved = await repository.ResolveAsync(Issuer, Subject, "   ");

        resolved.DisplayName.Should().Be("Ada Lovelace");
    }

    /// <summary>
    /// A renamed user is still tracked, which is why the write is made
    /// conditional rather than moved to <c>SetOnInsert</c>.
    /// </summary>
    [TestMethod]
    public async Task ALoginWithANewNameUpdatesTheStoredName()
    {
        UserIdentity first = await repository.ResolveAsync(Issuer, Subject, "Ada Lovelace");

        UserIdentity second = await repository.ResolveAsync(Issuer, Subject, "Ada King");

        second.UserId.Should().Be(first.UserId);
        second.DisplayName.Should().Be("Ada King");
        (await ReadStoredNameAsync()).Should().Be("Ada King");
    }

    [TestMethod]
    public async Task AFirstLoginWithoutANameStoresNoName()
    {
        UserIdentity resolved = await repository.ResolveAsync(Issuer, Subject, null);

        resolved.UserId.Should().NotBe(Guid.Empty);
        resolved.DisplayName.Should().BeNull();
    }

    /// <summary>
    /// Simultaneous first logins settle on one identity rather than two.
    /// </summary>
    /// <remarks>
    /// The upsert generates its own identifier, so without the unique index and
    /// the duplicate-key fallback beneath it the same person would end up with
    /// two user records and, from the second one, an empty TODO list. The racers
    /// are started together so the collision the fallback answers is the one
    /// under test.
    /// </remarks>
    [TestMethod]
    public async Task SimultaneousFirstLoginsResolveToOneIdentity()
    {
        Task<UserIdentity>[] racers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
                repository.ResolveAsync(Issuer, Subject, "Ada Lovelace")))
            .ToArray();

        UserIdentity[] resolved = await Task.WhenAll(racers);

        resolved.Select(identity => identity.UserId).Distinct().Should().HaveCount(1);
        (await CountStoredUsersAsync()).Should().Be(1);
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private IUserDirectoryRepository CreateRepository(string databaseName)
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            [$"{MongoDbSettings.SectionName}:ConnectionString"] =
                mongoDbContainer!.GetConnectionString(),
            [$"{MongoDbSettings.SectionName}:DatabaseName"] = databaseName,
        };
        ServiceCollection services = new ServiceCollection();

        // The hosted services started by TestInitialize log, and the index
        // initializer is only reachable through the whole IHostedService set.
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build());

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        providers.Add(serviceProvider);

        return serviceProvider.GetRequiredService<IUserDirectoryRepository>();
    }

    private async Task<long> CountStoredUsersAsync()
    {
        return await database
            .GetCollection<BsonDocument>("users")
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("issuer", Issuer)
                & Builders<BsonDocument>.Filter.Eq("subject", Subject));
    }

    private async Task<string?> ReadStoredNameAsync()
    {
        BsonDocument document = await database
            .GetCollection<BsonDocument>("users")
            .Find(Builders<BsonDocument>.Filter.Eq("issuer", Issuer)
                & Builders<BsonDocument>.Filter.Eq("subject", Subject))
            .SingleAsync();

        return document.TryGetValue("displayName", out BsonValue? name) && !name.IsBsonNull
            ? name.AsString
            : null;
    }
}
