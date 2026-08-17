using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MongoDB.Driver;

using Sleeky.Todo.Api.Contracts.Spaces;
using Sleeky.Todo.Api.Contracts.Todos;
using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Enums;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// The journey the feature exists for, driven entirely through the HTTP API:
/// Alice shares a list with Bob, changes what he may do in it, and takes it
/// back.
/// </summary>
/// <remarks>
/// The Space suite covers the access list as a resource of its own. What is
/// asserted here is the consequence — a grant is only worth making if the
/// TODOs behind it become reachable at the granted level, and only worth
/// revoking if they stop being reachable at all.
/// </remarks>
[TestClass]
public sealed class SpaceSharingApiTests
{
    private const string Issuer = "https://issuer.test/realms/sleeky";

    private static MongoDbContainer? mongoDbContainer;

    private HttpClient alice = null!;
    private Guid aliceId;
    private HttpClient bob = null!;
    private Guid bobId;
    private string databaseName = null!;
    private TodoApiFactory factory = null!;
    private Guid spaceId;

    private string Todos => $"/api/spaces/{spaceId}/todos";

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

    [TestInitialize]
    public async Task TestInitialize()
    {
        if (mongoDbContainer is null)
        {
            Assert.Inconclusive(
                "Set RUN_MONGODB_INTEGRATION_TESTS=true and start Docker to run API integration tests.");
        }

        databaseName = $"sleekyTodoSpaceSharingApiTests_{Guid.NewGuid():N}";
        factory = new TodoApiFactory(
            mongoDbContainer.GetConnectionString(),
            databaseName);

        aliceId = await SeedUserAsync("Alice Anderson", "alice@sleeky-todo.local");
        bobId = await SeedUserAsync("Bob Baxter", "bob@sleeky-todo.local");
        alice = await factory.CreateAuthenticatedClientAsync(aliceId);
        bob = await factory.CreateAuthenticatedClientAsync(bobId);

        using HttpResponseMessage created = await alice.PostAsJsonAsync(
            "/api/spaces",
            new CreateSpaceRequest { Name = "Project Alpha" });
        spaceId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        alice?.Dispose();
        bob?.Dispose();
        factory?.Dispose();

        if (mongoDbContainer is not null && databaseName is not null)
        {
            MongoClient mongoClient = new MongoClient(
                mongoDbContainer.GetConnectionString());
            await mongoClient.DropDatabaseAsync(databaseName);
        }
    }

    /// <summary>
    /// A Write grant is the whole of the headline requirement: the Space
    /// appears in the new member's own list, Alice's TODO is there when he
    /// opens it, and what he adds is there when Alice looks again.
    /// </summary>
    [TestMethod]
    public async Task AWriteGrantGivesTheNewMemberTheSpaceAndItsTodos()
    {
        Guid aliceTodoId = await CreateTodoAsync(alice, "Draft the brief");
        using HttpResponseMessage beforeResponse = await bob.GetAsync(Todos);

        using HttpResponseMessage grantResponse = await AddAccessAsync(
            bobId,
            SpacePermission.Write,
            version: 1);
        using HttpResponseMessage listResponse = await bob.GetAsync("/api/spaces");
        JsonElement bobSpaces = await ReadJsonAsync(listResponse);
        using HttpResponseMessage readResponse = await bob.GetAsync($"{Todos}/{aliceTodoId}");
        Guid bobTodoId = await CreateTodoAsync(bob, "Book the room");
        using HttpResponseMessage aliceSeesResponse = await alice.GetAsync(Todos);
        JsonElement aliceSees = await ReadJsonAsync(aliceSeesResponse);

        beforeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        bobSpaces.EnumerateArray()
            .Select(space => space.GetProperty("id").GetGuid())
            .Should().Contain(spaceId);
        bobSpaces.EnumerateArray()
            .Single(space => space.GetProperty("id").GetGuid() == spaceId)
            .GetProperty("permission").GetInt32()
            .Should().Be((int)SpacePermission.Write);
        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        Names(aliceSees).Should().BeEquivalentTo(["Draft the brief", "Book the room"]);
        CreatorsByName(aliceSees)["Book the room"].Should().Be(bobId);
        bobTodoId.Should().NotBe(aliceTodoId);
    }

    /// <summary>
    /// Downgrading a member takes their writes away without taking the Space
    /// away: they keep reading it, and every attempt to change it is refused
    /// as a permission problem rather than as a missing Space.
    /// </summary>
    [TestMethod]
    public async Task ADowngradeToReadLeavesTheSpaceVisibleAndTheTodosUntouchable()
    {
        Guid todoId = await CreateTodoAsync(alice, "Draft the brief");
        _ = await AddAccessAsync(bobId, SpacePermission.Write, version: 1);

        using HttpResponseMessage changeResponse = await alice.PutAsJsonAsync(
            $"/api/spaces/{spaceId}/access/{bobId}",
            new ChangeSpacePermissionRequest
            {
                Permission = SpacePermission.Read,
                Version = 2,
            });
        using HttpResponseMessage readResponse = await bob.GetAsync(Todos);
        using HttpResponseMessage createResponse = await bob.PostAsJsonAsync(
            Todos,
            NewTodo("Should not exist"));
        using HttpResponseMessage statusResponse = await bob.PutAsJsonAsync(
            $"{Todos}/{todoId}/status",
            new ChangeTodoStatusRequest { Status = TodoStatus.Completed, Version = 1 });
        using HttpResponseMessage unchangedResponse = await alice.GetAsync(Todos);

        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        statusResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Names(await ReadJsonAsync(unchangedResponse)).Should().Equal("Draft the brief");
    }

    /// <summary>
    /// A removed member is answered exactly as a stranger is: 404 for the
    /// Space and for everything under it, which does not confirm that either
    /// still exists.
    /// </summary>
    [TestMethod]
    public async Task ARemovedMemberLosesTheSpaceAndEverythingUnderIt()
    {
        Guid todoId = await CreateTodoAsync(alice, "Draft the brief");
        _ = await AddAccessAsync(bobId, SpacePermission.Write, version: 1);

        using HttpRequestMessage removal = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/spaces/{spaceId}/access/{bobId}")
        {
            Content = JsonContent.Create(new RemoveSpaceAccessRequest { Version = 2 }),
        };
        using HttpResponseMessage removeResponse = await alice.SendAsync(removal);
        using HttpResponseMessage spaceResponse = await bob.GetAsync($"/api/spaces/{spaceId}");
        using HttpResponseMessage accessResponse = await bob.GetAsync(
            $"/api/spaces/{spaceId}/access");
        using HttpResponseMessage listResponse = await bob.GetAsync(Todos);
        using HttpResponseMessage itemResponse = await bob.GetAsync($"{Todos}/{todoId}");
        using HttpResponseMessage createResponse = await bob.PostAsJsonAsync(
            Todos,
            NewTodo("Should not exist"));
        using HttpResponseMessage spacesResponse = await bob.GetAsync("/api/spaces");

        removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        spaceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        accessResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        listResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        itemResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        createResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadJsonAsync(spacesResponse)).EnumerateArray()
            .Select(space => space.GetProperty("id").GetGuid())
            .Should().NotContain(spaceId);
    }

    /// <summary>
    /// Sharing starts by finding someone, so the two halves are asserted
    /// together: the identifier the search hands back is the one the grant
    /// accepts.
    /// </summary>
    [TestMethod]
    public async Task TheIdentifierASearchReturnsIsTheOneAGrantTakes()
    {
        using HttpResponseMessage searchResponse = await alice.GetAsync("/api/users/search?q=bob");
        JsonElement results = await ReadJsonAsync(searchResponse);
        Guid foundId = results[0].GetProperty("id").GetGuid();

        using HttpResponseMessage grantResponse = await AddAccessAsync(
            foundId,
            SpacePermission.Read,
            version: 1);
        JsonElement space = await ReadJsonAsync(grantResponse);

        foundId.Should().Be(bobId);
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        space.GetProperty("access").EnumerateArray()
            .Select(entry => entry.GetProperty("subjectId").GetGuid())
            .Should().BeEquivalentTo([aliceId, bobId]);
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static CreateTodoRequest NewTodo(string name)
    {
        return new CreateTodoRequest
        {
            Name = name,
            DueDate = new DateOnly(2026, 9, 30),
            Priority = TodoPriority.Medium,
        };
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IEnumerable<string> Names(JsonElement page)
    {
        return page.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!);
    }

    private static Dictionary<string, Guid> CreatorsByName(JsonElement page)
    {
        return page.GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("name").GetString()!,
                item => item.GetProperty("createdByUserId").GetGuid());
    }

    private async Task<HttpResponseMessage> AddAccessAsync(
        Guid subjectId,
        SpacePermission permission,
        long version)
    {
        return await alice.PostAsJsonAsync(
            $"/api/spaces/{spaceId}/access",
            new AddSpaceAccessRequest
            {
                SubjectId = subjectId,
                Permission = permission,
                Version = version,
            });
    }

    private async Task<Guid> CreateTodoAsync(HttpClient member, string name)
    {
        using HttpResponseMessage response = await member.PostAsJsonAsync(Todos, NewTodo(name));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
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
