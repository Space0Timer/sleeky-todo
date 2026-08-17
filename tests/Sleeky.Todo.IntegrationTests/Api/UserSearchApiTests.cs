using System.Net;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// Finding the person to share with — the one route that answers a question
/// about somebody the caller has no relationship with yet.
/// </summary>
/// <remarks>
/// The assertions worth having here are as much about what the route will not
/// do as about what it will: a one-letter term, an unbounded result set, and a
/// user who has never signed in are each a way to read the directory rather
/// than to find a colleague.
/// </remarks>
[TestClass]
public sealed class UserSearchApiTests
{
    private const string Issuer = "https://issuer.test/realms/sleeky";

    private static MongoDbContainer? mongoDbContainer;

    private HttpClient alice = null!;
    private Guid aliceId;
    private string databaseName = null!;
    private TodoApiFactory factory = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext testContext)
    {
        if (!ShouldRunMongoDbTests())
        {
            return;
        }

        mongoDbContainer = new MongoDbBuilder("mongo:8.0")
            .WithReplicaSet("rs0")
            .Build();
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
    /// The caller is registered in the directory the way a first sign-in
    /// would, and then authenticates as the identifier that produced — the
    /// test authentication handler never writes to the directory, so a client
    /// built from an arbitrary identifier is a user the directory has never
    /// heard of.
    /// </summary>
    [TestInitialize]
    public async Task TestInitialize()
    {
        if (mongoDbContainer is null)
        {
            Assert.Inconclusive(
                "Set RUN_MONGODB_INTEGRATION_TESTS=true and start Docker to run API integration tests.");
        }

        databaseName = $"sleekyTodoUserSearchApiTests_{Guid.NewGuid():N}";
        factory = new TodoApiFactory(
            mongoDbContainer.GetConnectionString(),
            databaseName);

        aliceId = await SeedUserAsync("Alice Anderson", "alice@sleeky-todo.local");
        alice = await factory.CreateAuthenticatedClientAsync(aliceId);
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        alice?.Dispose();
        factory?.Dispose();

        if (mongoDbContainer is not null && databaseName is not null)
        {
            MongoClient mongoClient = new MongoClient(
                mongoDbContainer.GetConnectionString());
            await mongoClient.DropDatabaseAsync(databaseName);
        }
    }

    [TestMethod]
    public async Task SearchMatchesTheStartOfADisplayName()
    {
        Guid bobId = await SeedUserAsync("Bob Baxter", "bob@sleeky-todo.local");
        _ = await SeedUserAsync("Carol Chen", "carol@sleeky-todo.local");

        using HttpResponseMessage response = await alice.GetAsync("/api/users/search?q=Bob");
        JsonElement results = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        results.GetArrayLength().Should().Be(1);
        results[0].GetProperty("id").GetGuid().Should().Be(bobId);
        results[0].GetProperty("displayName").GetString().Should().Be("Bob Baxter");
        results[0].GetProperty("email").GetString().Should().Be("bob@sleeky-todo.local");
    }

    [TestMethod]
    public async Task SearchMatchesTheStartOfAnEmailAddress()
    {
        Guid bobId = await SeedUserAsync("Bob Baxter", "bob@sleeky-todo.local");

        using HttpResponseMessage response = await alice.GetAsync(
            "/api/users/search?q=bob%40sleeky");
        JsonElement results = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Ids(results).Should().Equal(bobId);
    }

    [TestMethod]
    public async Task SearchIgnoresCase()
    {
        Guid bobId = await SeedUserAsync("Bob Baxter", "bob@sleeky-todo.local");

        using HttpResponseMessage lowerResponse = await alice.GetAsync("/api/users/search?q=bob");
        using HttpResponseMessage upperResponse = await alice.GetAsync("/api/users/search?q=BOB");
        using HttpResponseMessage mixedResponse = await alice.GetAsync("/api/users/search?q=bOb");

        Ids(await ReadJsonAsync(lowerResponse)).Should().Equal(bobId);
        Ids(await ReadJsonAsync(upperResponse)).Should().Equal(bobId);
        Ids(await ReadJsonAsync(mixedResponse)).Should().Equal(bobId);
    }

    /// <summary>
    /// The searcher is never among the answers: sharing a Space with yourself
    /// is not a thing to offer, and the entry would sit at the top of a list
    /// where it is only in the way.
    /// </summary>
    [TestMethod]
    public async Task SearchLeavesTheCallerOut()
    {
        Guid otherId = await SeedUserAsync("Alice Archer", "alice.archer@sleeky-todo.local");

        using HttpResponseMessage response = await alice.GetAsync("/api/users/search?q=alice");
        JsonElement results = await ReadJsonAsync(response);

        Ids(results).Should().Equal(otherId);
        Ids(results).Should().NotContain(aliceId);
    }

    /// <summary>
    /// The directory holds only users who have signed in at least once, which
    /// is the same rule that decides who can be granted access to a Space: a
    /// colleague who has never opened the application cannot be found and
    /// cannot be shared with.
    /// </summary>
    [TestMethod]
    public async Task SearchNeverReturnsAUserWhoHasNotSignedIn()
    {
        Guid knownId = await SeedUserAsync("Tessa Tan", "tessa@sleeky-todo.local");
        Guid neverSignedInId = Guid.NewGuid();
        using HttpClient stranger = await factory.CreateAuthenticatedClientAsync(
            neverSignedInId);
        _ = await stranger.GetAsync("/api/spaces");

        using HttpResponseMessage response = await alice.GetAsync("/api/users/search?q=te");
        JsonElement results = await ReadJsonAsync(response);

        // A live session and a personal Space are not a directory entry: only
        // the identity-provider handshake writes one, so the user the search
        // finds is the one that went through it.
        Ids(results).Should().Equal(knownId);
        Ids(results).Should().NotContain(neverSignedInId);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("a")]
    [DataRow("  a  ")]
    public async Task SearchRefusesATermShorterThanTwoCharacters(string term)
    {
        using HttpResponseMessage response = await alice.GetAsync(
            $"/api/users/search?q={Uri.EscapeDataString(term)}");
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("title").GetString().Should().Be("Validation failed.");
        problem.GetProperty("errors").TryGetProperty("query", out _).Should().BeTrue();
    }

    [TestMethod]
    public async Task SearchWithNoTermAtAllIsRejectedRatherThanListingEveryone()
    {
        using HttpResponseMessage response = await alice.GetAsync("/api/users/search");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A term that matches broadly still answers with a handful. Twelve are
    /// seeded so the cap is exercised from both sides: more than ten match,
    /// and exactly ten come back.
    /// </summary>
    [TestMethod]
    public async Task SearchReturnsAtMostTenResults()
    {
        for (int index = 0; index < 12; index++)
        {
            _ = await SeedUserAsync($"Sample {index:D2}", $"sample{index:D2}@sleeky-todo.local");
        }

        using HttpResponseMessage response = await alice.GetAsync("/api/users/search?q=sample");
        JsonElement results = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        results.GetArrayLength().Should().Be(10);
    }

    [TestMethod]
    public async Task SearchMatchesAPrefixRatherThanASubstring()
    {
        _ = await SeedUserAsync("Bob Baxter", "bob@sleeky-todo.local");

        using HttpResponseMessage response = await alice.GetAsync("/api/users/search?q=axter");
        JsonElement results = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        results.GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    public async Task SearchIsClosedToAnUnauthenticatedCaller()
    {
        using HttpClient anonymous = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });

        using HttpResponseMessage response = await anonymous.GetAsync("/api/users/search?q=bob");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IEnumerable<Guid> Ids(JsonElement results)
    {
        return results.EnumerateArray().Select(result => result.GetProperty("id").GetGuid());
    }

    private async Task<Guid> SeedUserAsync(string displayName, string email)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IUserDirectoryRepository users = scope.ServiceProvider
            .GetRequiredService<IUserDirectoryRepository>();
        UserIdentity identity = await users.ResolveAsync(
            Issuer,
            $"subject-{email}",
            displayName,
            email);

        return identity.UserId;
    }
}
