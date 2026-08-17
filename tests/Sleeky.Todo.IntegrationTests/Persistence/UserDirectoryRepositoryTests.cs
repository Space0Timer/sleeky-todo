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
public sealed class UserDirectoryRepositoryTests
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
        UserIdentity first = await repository.ResolveAsync(Issuer, Subject, "Ada Lovelace", "ada@sleeky.test");

        UserIdentity second = await repository.ResolveAsync(Issuer, Subject, null, null);

        second.UserId.Should().Be(first.UserId);
        second.DisplayName.Should().Be("Ada Lovelace");
        (await ReadStoredFieldAsync("displayName")).Should().Be("Ada Lovelace");
    }

    [TestMethod]
    public async Task ALoginWithABlankNameClaimKeepsTheStoredName()
    {
        _ = await repository.ResolveAsync(Issuer, Subject, "Ada Lovelace", "ada@sleeky.test");

        UserIdentity resolved = await repository.ResolveAsync(Issuer, Subject, "   ", "   ");

        resolved.DisplayName.Should().Be("Ada Lovelace");
    }

    /// <summary>
    /// A renamed user is still tracked, which is why the write is made
    /// conditional rather than moved to <c>SetOnInsert</c>.
    /// </summary>
    [TestMethod]
    public async Task ALoginWithANewNameUpdatesTheStoredName()
    {
        UserIdentity first = await repository.ResolveAsync(Issuer, Subject, "Ada Lovelace", "ada@sleeky.test");

        UserIdentity second = await repository.ResolveAsync(Issuer, Subject, "Ada King", null);

        second.UserId.Should().Be(first.UserId);
        second.DisplayName.Should().Be("Ada King");
        (await ReadStoredFieldAsync("displayName")).Should().Be("Ada King");
    }

    [TestMethod]
    public async Task AFirstLoginWithoutANameStoresNoName()
    {
        UserIdentity resolved = await repository.ResolveAsync(Issuer, Subject, null, null);

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
                repository.ResolveAsync(Issuer, Subject, "Ada Lovelace", "ada@sleeky.test")))
            .ToArray();

        UserIdentity[] resolved = await Task.WhenAll(racers);

        resolved.Select(identity => identity.UserId).Distinct().Should().HaveCount(1);
        (await CountStoredUsersAsync()).Should().Be(1);
    }

    /// <summary>
    /// A lookup answers only for identifiers the directory knows: an unknown
    /// one is left out rather than reported, so a caller cannot learn anything
    /// about a user who has never signed in.
    /// </summary>
    [TestMethod]
    public async Task FindByIdsAsyncReturnsOnlyTheIdentitiesTheDirectoryKnows()
    {
        UserIdentity ada = await repository.ResolveAsync(Issuer, "subject-ada", "Ada Lovelace", "ada@sleeky.test");
        UserIdentity grace = await repository.ResolveAsync(Issuer, "subject-grace", "Grace Hopper", "grace@sleeky.test");
        UserIdentity nameless = await repository.ResolveAsync(Issuer, "subject-nameless", null, null);
        _ = await repository.ResolveAsync(Issuer, "subject-other", "Someone Else", null);

        IReadOnlyCollection<UserIdentity> found = await repository.FindByIdsAsync(
            [ada.UserId, grace.UserId, nameless.UserId, Guid.NewGuid()]);

        found.Should().BeEquivalentTo(
        [
            new UserIdentity(ada.UserId, "Ada Lovelace"),
            new UserIdentity(grace.UserId, "Grace Hopper"),
            new UserIdentity(nameless.UserId, null),
        ]);
    }

    [TestMethod]
    public async Task FindByIdsAsyncWithNoIdentifiersReturnsNothing()
    {
        _ = await repository.ResolveAsync(Issuer, Subject, "Ada Lovelace", "ada@sleeky.test");

        IReadOnlyCollection<UserIdentity> found = await repository.FindByIdsAsync(Array.Empty<Guid>());

        found.Should().BeEmpty();
    }

    /// <summary>
    /// The address follows the same rule as the name: a token that omits it
    /// leaves what is stored alone, so a user does not become unfindable
    /// because one login carried a thinner set of claims.
    /// </summary>
    [TestMethod]
    public async Task ALoginWithoutAnEmailClaimKeepsTheStoredEmail()
    {
        _ = await repository.ResolveAsync(Issuer, Subject, "Ada Lovelace", "ada@sleeky.test");

        _ = await repository.ResolveAsync(Issuer, Subject, "Ada Lovelace", null);

        (await ReadStoredFieldAsync("email")).Should().Be("ada@sleeky.test");
        (await ReadStoredFieldAsync("emailNormalized")).Should().Be("ada@sleeky.test");
    }

    /// <summary>
    /// The searchable copies are written from the originals, lower-cased, and
    /// are replaced whenever the original is.
    /// </summary>
    [TestMethod]
    public async Task ResolveStoresLowerCasedCopiesOfTheNameAndAddress()
    {
        _ = await repository.ResolveAsync(Issuer, Subject, "Ada Lovelace", "Ada@Sleeky.TEST");

        (await ReadStoredFieldAsync("displayNameNormalized")).Should().Be("ada lovelace");
        (await ReadStoredFieldAsync("emailNormalized")).Should().Be("ada@sleeky.test");

        _ = await repository.ResolveAsync(Issuer, Subject, "Ada King", "Ada.King@Sleeky.TEST");

        (await ReadStoredFieldAsync("displayNameNormalized")).Should().Be("ada king");
        (await ReadStoredFieldAsync("emailNormalized")).Should().Be("ada.king@sleeky.test");
    }

    [TestMethod]
    public async Task SearchMatchesTheStartOfANameOrAnAddressWhateverTheCase()
    {
        UserIdentity ada = await repository.ResolveAsync(
            Issuer,
            "subject-ada",
            "Ada Lovelace",
            "ada@sleeky.test");
        UserIdentity grace = await repository.ResolveAsync(
            Issuer,
            "subject-grace",
            "Grace Hopper",
            "grace@sleeky.test");

        IReadOnlyCollection<UserSearchMatch> byName = await repository.SearchAsync("aDa", 10);
        IReadOnlyCollection<UserSearchMatch> byAddress = await repository.SearchAsync("GRACE@", 10);
        IReadOnlyCollection<UserSearchMatch> bySubstring = await repository.SearchAsync("ovelace", 10);

        byName.Should().BeEquivalentTo(
            [new UserSearchMatch(ada.UserId, "Ada Lovelace", "ada@sleeky.test")]);
        byAddress.Should().BeEquivalentTo(
            [new UserSearchMatch(grace.UserId, "Grace Hopper", "grace@sleeky.test")]);

        // A prefix, not a contains: the anchored form is what an index can
        // answer, and the whole point of storing the normalised copies.
        bySubstring.Should().BeEmpty();
    }

    /// <summary>
    /// A user recorded before the searchable copies existed is not found.
    /// </summary>
    /// <remarks>
    /// Accepted rather than migrated: their next sign-in writes both copies,
    /// so the gap closes by itself and a backfill would buy only the window
    /// between deploying and logging in.
    /// </remarks>
    [TestMethod]
    public async Task SearchDoesNotFindADocumentWrittenBeforeTheNormalisedCopies()
    {
        await database.GetCollection<BsonDocument>("users").InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["issuer"] = Issuer,
            ["subject"] = "subject-legacy",
            ["displayName"] = "Legacy Larsson",
            ["createdAt"] = DateTime.UtcNow,
            ["lastLoginAt"] = DateTime.UtcNow,
        });

        IReadOnlyCollection<UserSearchMatch> found = await repository.SearchAsync("legacy", 10);

        found.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SearchReturnsNoMoreThanTheLimit()
    {
        for (int index = 0; index < 5; index++)
        {
            _ = await repository.ResolveAsync(
                Issuer,
                $"subject-{index}",
                $"Sample {index}",
                $"sample{index}@sleeky.test");
        }

        IReadOnlyCollection<UserSearchMatch> found = await repository.SearchAsync("sample", 2);

        found.Should().HaveCount(2);
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

    private async Task<string?> ReadStoredFieldAsync(string field)
    {
        BsonDocument document = await database
            .GetCollection<BsonDocument>("users")
            .Find(Builders<BsonDocument>.Filter.Eq("issuer", Issuer)
                & Builders<BsonDocument>.Filter.Eq("subject", Subject))
            .SingleAsync();

        return document.TryGetValue(field, out BsonValue? value) && !value.IsBsonNull
            ? value.AsString
            : null;
    }
}
