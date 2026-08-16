using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;
using Sleeky.Todo.Infrastructure.DependencyInjection;
using Sleeky.Todo.Infrastructure.Persistence;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Persistence;

[TestClass]
public sealed class SpaceRepositoryTests
{
    private const string SpacesCollectionName = "spaces";

    private static readonly DateTimeOffset Timestamp = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterTimestamp = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid AliceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid BobId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid StrangerId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private static MongoDbContainer? mongoDbContainer;

    private readonly List<ServiceProvider> providers = new List<ServiceProvider>();

    private IMongoDatabase database = null!;
    private ISpaceRepository repository = null!;

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
    public async Task TestInitialize()
    {
        if (mongoDbContainer is null)
        {
            Assert.Inconclusive(
                "Set RUN_MONGODB_INTEGRATION_TESTS=true and start Docker to run MongoDB Space repository tests.");
        }

        MongoClient client = new MongoClient(mongoDbContainer.GetConnectionString());
        string databaseName = $"sleekyTodoSpaceTests_{Guid.NewGuid():N}";
        database = client.GetDatabase(databaseName);
        repository = CreateRepository(databaseName);

        foreach (IHostedService hostedService in providers[^1].GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        foreach (ServiceProvider provider in providers)
        {
            provider.Dispose();
        }

        providers.Clear();

        if (mongoDbContainer is not null)
        {
            await database.Client.DropDatabaseAsync(database.DatabaseNamespace.DatabaseName);
        }
    }

    [TestMethod]
    public async Task AddAsyncThenGetByIdAsyncRoundTripsTheSpace()
    {
        Space space = Space.Create(Guid.NewGuid(), "Project Alpha", AliceId, Timestamp);
        space.AddAccess(BobId, SubjectType.User, SpacePermission.Write, Timestamp);

        await repository.AddAsync(space);
        Space? stored = await repository.GetByIdAsync(space.Id);

        stored.Should().NotBeNull();
        stored.Id.Should().Be(space.Id);
        stored.Name.Should().Be("Project Alpha");
        stored.Access.Should().BeEquivalentTo(
        [
            new SpaceAccessEntry(AliceId, SubjectType.User, SpacePermission.Owner),
            new SpaceAccessEntry(BobId, SubjectType.User, SpacePermission.Write),
        ]);
        stored.Version.Should().Be(1);
        stored.CreatedAt.Should().Be(Timestamp);
        stored.UpdatedAt.Should().Be(Timestamp);
    }

    [TestMethod]
    public async Task GetByIdAsyncReturnsNullForAnUnknownSpace()
    {
        Space? stored = await repository.GetByIdAsync(Guid.NewGuid());

        stored.Should().BeNull();
    }

    /// <summary>
    /// The second caller gets what the first one stored, not what it brought:
    /// that is what lets a personal Space be renamed and still be "ensured" on
    /// every later request without the rename being undone.
    /// </summary>
    [TestMethod]
    public async Task GetOrAddAsyncReturnsTheStoredSpaceOnASecondCall()
    {
        Guid spaceId = Guid.NewGuid();
        Space first = Space.Create(spaceId, "My Space", AliceId, Timestamp);
        Space second = Space.Create(spaceId, "Renamed Elsewhere", AliceId, LaterTimestamp);

        Space firstResult = await repository.GetOrAddAsync(first);
        Space secondResult = await repository.GetOrAddAsync(second);

        firstResult.Should().BeSameAs(first);
        secondResult.Name.Should().Be("My Space");
        secondResult.CreatedAt.Should().Be(Timestamp);
        (await CountStoredSpacesAsync(spaceId)).Should().Be(1);
    }

    /// <summary>
    /// The race the derived identifier exists for: many first requests, one
    /// document. The insert-then-read-on-collision shape is what makes every
    /// racer come away holding the same Space.
    /// </summary>
    [TestMethod]
    public async Task SimultaneousGetOrAddCallsConvergeOnOneDocument()
    {
        Guid spaceId = Guid.NewGuid();
        Task<Space>[] racers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
                repository.GetOrAddAsync(Space.Create(spaceId, "My Space", AliceId, Timestamp))))
            .ToArray();

        Space[] results = await Task.WhenAll(racers);

        results.Select(space => space.Id).Distinct().Should().ContainSingle().Which.Should().Be(spaceId);
        (await CountStoredSpacesAsync(spaceId)).Should().Be(1);
    }

    [TestMethod]
    public async Task GetForSubjectAsyncReturnsOnlyMembershipsOldestFirst()
    {
        Space aliceOnly = Space.Create(Guid.NewGuid(), "Alice", AliceId, Timestamp);
        Space shared = Space.Create(Guid.NewGuid(), "Shared", BobId, Timestamp.AddMinutes(1));
        shared.AddAccess(AliceId, SubjectType.User, SpacePermission.Read, Timestamp.AddMinutes(1));
        Space bobOnly = Space.Create(Guid.NewGuid(), "Bob", BobId, Timestamp.AddMinutes(2));
        await repository.AddAsync(shared);
        await repository.AddAsync(bobOnly);
        await repository.AddAsync(aliceOnly);

        IReadOnlyCollection<Space> forAlice = await repository.GetForSubjectAsync(AliceId, SubjectType.User);
        IReadOnlyCollection<Space> forBob = await repository.GetForSubjectAsync(BobId, SubjectType.User);
        IReadOnlyCollection<Space> forStranger = await repository.GetForSubjectAsync(StrangerId, SubjectType.User);

        forAlice.Select(space => space.Name).Should().Equal("Alice", "Shared");
        forBob.Select(space => space.Name).Should().Equal("Shared", "Bob");
        forStranger.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateAsyncPersistsTheChangeAndAdvancesTheVersion()
    {
        Space space = Space.Create(Guid.NewGuid(), "Project Alpha", AliceId, Timestamp);
        await repository.AddAsync(space);
        space.Rename("Project Beta", LaterTimestamp);
        space.AddAccess(BobId, SubjectType.User, SpacePermission.Read, LaterTimestamp);

        Space updated = await repository.UpdateAsync(space);
        Space? stored = await repository.GetByIdAsync(space.Id);

        updated.Version.Should().Be(2);
        updated.Name.Should().Be("Project Beta");
        stored.Should().NotBeNull();
        stored.Version.Should().Be(2);
        stored.Name.Should().Be("Project Beta");
        stored.PermissionFor(BobId, SubjectType.User).Should().Be(SpacePermission.Read);
        stored.UpdatedAt.Should().Be(LaterTimestamp);
    }

    /// <summary>
    /// Two Owners editing the access list from the same version: the second
    /// write must not silently undo the first, so it is refused with the
    /// version it expected, exactly as a stale TODO write is.
    /// </summary>
    [TestMethod]
    public async Task UpdateAsyncWithAStaleVersionThrowsAConcurrencyConflict()
    {
        Space space = Space.Create(Guid.NewGuid(), "Project Alpha", AliceId, Timestamp);
        await repository.AddAsync(space);
        Space firstCopy = (await repository.GetByIdAsync(space.Id))!;
        Space secondCopy = (await repository.GetByIdAsync(space.Id))!;
        firstCopy.AddAccess(BobId, SubjectType.User, SpacePermission.Write, LaterTimestamp);
        secondCopy.Rename("Project Beta", LaterTimestamp);
        await repository.UpdateAsync(firstCopy);

        Func<Task> act = () => repository.UpdateAsync(secondCopy);

        ConcurrencyConflictException exception = (await act.Should()
            .ThrowAsync<ConcurrencyConflictException>())
            .Which;
        exception.ResourceName.Should().Be("Space");
        exception.ResourceId.Should().Be(space.Id);
        exception.ExpectedVersion.Should().Be(1);
        Space? stored = await repository.GetByIdAsync(space.Id);
        stored!.Name.Should().Be("Project Alpha");
        stored.PermissionFor(BobId, SubjectType.User).Should().Be(SpacePermission.Write);
    }

    /// <summary>
    /// The membership lookup is the one query every request makes, so its
    /// index has to come from startup rather than from someone noticing a
    /// collection scan later.
    /// </summary>
    [TestMethod]
    public async Task StartupCreatesTheMembershipIndex()
    {
        using IAsyncCursor<BsonDocument> cursor = await database
            .GetCollection<BsonDocument>(SpacesCollectionName)
            .Indexes.ListAsync();
        List<BsonDocument> indexes = await cursor.ToListAsync();

        indexes.Select(index => index["name"].AsString).Should().Contain("access_subject");
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private ISpaceRepository CreateRepository(string databaseName)
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            [$"{MongoDbSettings.SectionName}:ConnectionString"] =
                mongoDbContainer!.GetConnectionString(),
            [$"{MongoDbSettings.SectionName}:DatabaseName"] = databaseName,
        };
        ServiceCollection services = new ServiceCollection();

        // The hosted services started by TestInitialize log, and the index
        // initializers are only reachable through the whole IHostedService set.
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build());

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        providers.Add(serviceProvider);

        return serviceProvider.GetRequiredService<ISpaceRepository>();
    }

    private async Task<long> CountStoredSpacesAsync(Guid spaceId)
    {
        return await database
            .GetCollection<BsonDocument>(SpacesCollectionName)
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq(
                "_id",
                new BsonBinaryData(spaceId, GuidRepresentation.Standard)));
    }
}
