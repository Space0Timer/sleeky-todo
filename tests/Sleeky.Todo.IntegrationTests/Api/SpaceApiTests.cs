using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MongoDB.Driver;

using Sleeky.Todo.Api.Contracts.Spaces;
using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Spaces;
using Sleeky.Todo.Domain.Enums;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Api;

[TestClass]
public sealed class SpaceApiTests
{
    private const string Issuer = "https://issuer.test/realms/sleeky";

    private static readonly Guid AliceId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid StrangerId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static MongoDbContainer? mongoDbContainer;

    private HttpClient alice = null!;
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

    [TestInitialize]
    public async Task TestInitialize()
    {
        if (mongoDbContainer is null)
        {
            Assert.Inconclusive(
                "Set RUN_MONGODB_INTEGRATION_TESTS=true and start Docker to run API integration tests.");
        }

        databaseName = $"sleekyTodoSpaceApiTests_{Guid.NewGuid():N}";
        factory = new TodoApiFactory(
            mongoDbContainer.GetConnectionString(),
            databaseName);
        alice = await factory.CreateAuthenticatedClientAsync(AliceId);
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
    public async Task GetSpacesCreatesThePersonalSpaceOnceAndListsOnlyTheCallersSpaces()
    {
        using HttpClient stranger = await factory.CreateAuthenticatedClientAsync(StrangerId);

        HttpResponseMessage firstResponse = await alice.GetAsync("/api/spaces");
        JsonElement first = await ReadJsonAsync(firstResponse);
        HttpResponseMessage secondResponse = await alice.GetAsync("/api/spaces");
        JsonElement second = await ReadJsonAsync(secondResponse);
        HttpResponseMessage strangerResponse = await stranger.GetAsync("/api/spaces");
        JsonElement strangerSpaces = await ReadJsonAsync(strangerResponse);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        first.GetArrayLength().Should().Be(1);
        first[0].GetProperty("id").GetGuid().Should().Be(PersonalSpace.IdFor(AliceId));
        first[0].GetProperty("name").GetString().Should().Be(PersonalSpace.Name);
        first[0].GetProperty("permission").GetInt32().Should().Be((int)SpacePermission.Owner);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        second.GetArrayLength().Should().Be(1);
        second[0].GetProperty("id").GetGuid().Should().Be(PersonalSpace.IdFor(AliceId));
        strangerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        strangerSpaces.GetArrayLength().Should().Be(1);
        strangerSpaces[0].GetProperty("id").GetGuid().Should().Be(PersonalSpace.IdFor(StrangerId));
    }

    [TestMethod]
    public async Task PostCreatesASpaceWithTheCallerAsOwner()
    {
        (HttpResponseMessage createResponse, JsonElement created) = await CreateSpaceAsync();
        Guid spaceId = created.GetProperty("id").GetGuid();

        HttpResponseMessage getResponse = await alice.GetAsync($"/api/spaces/{spaceId}");
        JsonElement retrieved = await ReadJsonAsync(getResponse);
        HttpResponseMessage listResponse = await alice.GetAsync("/api/spaces");
        JsonElement list = await ReadJsonAsync(listResponse);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        createResponse.Headers.Location.Should().NotBeNull();
        createResponse.Headers.Location!.ToString().Should().EndWith($"/api/spaces/{spaceId}");
        created.GetProperty("name").GetString().Should().Be("Project Alpha");
        created.GetProperty("permission").GetInt32().Should().Be((int)SpacePermission.Owner);
        created.GetProperty("version").GetInt64().Should().Be(1);
        JsonElement access = created.GetProperty("access");
        access.GetArrayLength().Should().Be(1);
        access[0].GetProperty("subjectId").GetGuid().Should().Be(AliceId);
        access[0].GetProperty("subjectType").GetInt32().Should().Be((int)SubjectType.User);
        access[0].GetProperty("permission").GetInt32().Should().Be((int)SpacePermission.Owner);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        retrieved.GetProperty("id").GetGuid().Should().Be(spaceId);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        list.EnumerateArray().Select(space => space.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo([PersonalSpace.IdFor(AliceId), spaceId]);
    }

    [TestMethod]
    public async Task PostWithABlankNameIsRejected()
    {
        HttpResponseMessage response = await alice.PostAsJsonAsync(
            "/api/spaces",
            new CreateSpaceRequest { Name = "   " });
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("title").GetString().Should().Be("Validation failed.");
        problem.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
    }

    [TestMethod]
    public async Task ANonMemberIsNotFoundOnEverySpaceRoute()
    {
        (_, JsonElement created) = await CreateSpaceAsync();
        Guid spaceId = created.GetProperty("id").GetGuid();
        using HttpClient stranger = await factory.CreateAuthenticatedClientAsync(StrangerId);

        HttpResponseMessage getResponse = await stranger.GetAsync($"/api/spaces/{spaceId}");
        HttpResponseMessage accessResponse = await stranger.GetAsync($"/api/spaces/{spaceId}/access");
        HttpResponseMessage renameResponse = await stranger.PutAsJsonAsync(
            $"/api/spaces/{spaceId}",
            new RenameSpaceRequest { Name = "Hijacked", Version = 1 });
        HttpResponseMessage addResponse = await stranger.PostAsJsonAsync(
            $"/api/spaces/{spaceId}/access",
            new AddSpaceAccessRequest
            {
                SubjectId = StrangerId,
                Permission = SpacePermission.Owner,
                Version = 1,
            });
        HttpResponseMessage changeResponse = await stranger.PutAsJsonAsync(
            $"/api/spaces/{spaceId}/access/{AliceId}",
            new ChangeSpacePermissionRequest { Permission = SpacePermission.Read, Version = 1 });
        HttpResponseMessage removeResponse = await RemoveAccessAsync(stranger, spaceId, AliceId, 1);
        JsonElement problem = await ReadJsonAsync(getResponse);

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        problem.GetProperty("title").GetString().Should().Be("Resource not found.");
        accessResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        renameResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        addResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        changeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    [DataRow(SpacePermission.Read)]
    [DataRow(SpacePermission.Write)]
    public async Task AMemberBelowOwnerCanReadButNotChangeTheSpace(SpacePermission held)
    {
        (_, JsonElement created) = await CreateSpaceAsync();
        Guid spaceId = created.GetProperty("id").GetGuid();
        Guid bobId = await SeedUserAsync("Bob");
        Guid carolId = await SeedUserAsync("Carol");
        _ = await AddAccessAsync(alice, spaceId, bobId, held, version: 1);
        using HttpClient bob = await factory.CreateAuthenticatedClientAsync(bobId);

        HttpResponseMessage getResponse = await bob.GetAsync($"/api/spaces/{spaceId}");
        JsonElement retrieved = await ReadJsonAsync(getResponse);
        HttpResponseMessage accessResponse = await bob.GetAsync($"/api/spaces/{spaceId}/access");
        HttpResponseMessage renameResponse = await bob.PutAsJsonAsync(
            $"/api/spaces/{spaceId}",
            new RenameSpaceRequest { Name = "Project Beta", Version = 2 });
        JsonElement problem = await ReadJsonAsync(renameResponse);
        HttpResponseMessage addResponse = await AddAccessAsync(
            bob,
            spaceId,
            carolId,
            SpacePermission.Read,
            version: 2);
        HttpResponseMessage changeResponse = await bob.PutAsJsonAsync(
            $"/api/spaces/{spaceId}/access/{bobId}",
            new ChangeSpacePermissionRequest { Permission = SpacePermission.Owner, Version = 2 });
        HttpResponseMessage removeResponse = await RemoveAccessAsync(bob, spaceId, AliceId, 2);
        HttpResponseMessage unchangedResponse = await alice.GetAsync($"/api/spaces/{spaceId}");
        JsonElement unchanged = await ReadJsonAsync(unchangedResponse);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        retrieved.GetProperty("permission").GetInt32().Should().Be((int)held);
        accessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        renameResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        problem.GetProperty("title").GetString().Should().Be("Forbidden.");
        addResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        changeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        removeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unchanged.GetProperty("name").GetString().Should().Be("Project Alpha");
        unchanged.GetProperty("version").GetInt64().Should().Be(2);
        unchanged.GetProperty("access").GetArrayLength().Should().Be(2);
    }

    [TestMethod]
    public async Task AnOwnerCanAddChangeAndRemoveAccess()
    {
        (_, JsonElement created) = await CreateSpaceAsync();
        Guid spaceId = created.GetProperty("id").GetGuid();
        Guid bobId = await SeedUserAsync("Bob");
        using HttpClient bob = await factory.CreateAuthenticatedClientAsync(bobId);

        HttpResponseMessage addResponse = await AddAccessAsync(
            alice,
            spaceId,
            bobId,
            SpacePermission.Read,
            version: 1);
        JsonElement added = await ReadJsonAsync(addResponse);
        HttpResponseMessage bobSeesResponse = await bob.GetAsync($"/api/spaces/{spaceId}");
        HttpResponseMessage changeResponse = await alice.PutAsJsonAsync(
            $"/api/spaces/{spaceId}/access/{bobId}",
            new ChangeSpacePermissionRequest { Permission = SpacePermission.Write, Version = 2 });
        JsonElement changed = await ReadJsonAsync(changeResponse);
        HttpResponseMessage removeResponse = await RemoveAccessAsync(alice, spaceId, bobId, 3);
        JsonElement removed = await ReadJsonAsync(removeResponse);
        HttpResponseMessage bobGoneResponse = await bob.GetAsync($"/api/spaces/{spaceId}");

        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        added.GetProperty("version").GetInt64().Should().Be(2);
        added.GetProperty("permission").GetInt32().Should().Be((int)SpacePermission.Owner);
        JsonElement bobEntry = FindAccessEntry(added, bobId);
        bobEntry.GetProperty("permission").GetInt32().Should().Be((int)SpacePermission.Read);
        bobEntry.GetProperty("displayName").GetString().Should().Be("Bob");
        bobSeesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        changed.GetProperty("version").GetInt64().Should().Be(3);
        FindAccessEntry(changed, bobId).GetProperty("permission").GetInt32()
            .Should().Be((int)SpacePermission.Write);
        removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        removed.GetProperty("version").GetInt64().Should().Be(4);
        removed.GetProperty("access").EnumerateArray()
            .Select(entry => entry.GetProperty("subjectId").GetGuid())
            .Should().Equal(AliceId);
        bobGoneResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task ANewMemberSeesTheSpaceInTheirOwnList()
    {
        (_, JsonElement created) = await CreateSpaceAsync();
        Guid spaceId = created.GetProperty("id").GetGuid();
        Guid bobId = await SeedUserAsync("Bob");
        using HttpClient bob = await factory.CreateAuthenticatedClientAsync(bobId);
        HttpResponseMessage beforeResponse = await bob.GetAsync("/api/spaces");
        JsonElement before = await ReadJsonAsync(beforeResponse);

        _ = await AddAccessAsync(alice, spaceId, bobId, SpacePermission.Write, version: 1);
        HttpResponseMessage afterResponse = await bob.GetAsync("/api/spaces");
        JsonElement after = await ReadJsonAsync(afterResponse);

        before.EnumerateArray().Select(space => space.GetProperty("id").GetGuid())
            .Should().Equal(PersonalSpace.IdFor(bobId));
        afterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        after.EnumerateArray().Select(space => space.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo([PersonalSpace.IdFor(bobId), spaceId]);
        JsonElement shared = after.EnumerateArray()
            .Single(space => space.GetProperty("id").GetGuid() == spaceId);
        shared.GetProperty("name").GetString().Should().Be("Project Alpha");
        shared.GetProperty("permission").GetInt32().Should().Be((int)SpacePermission.Write);
    }

    [TestMethod]
    public async Task GetAccessListsEveryMemberWithTheirDisplayName()
    {
        (_, JsonElement created) = await CreateSpaceAsync();
        Guid spaceId = created.GetProperty("id").GetGuid();
        Guid bobId = await SeedUserAsync("Bob");
        _ = await AddAccessAsync(alice, spaceId, bobId, SpacePermission.Read, version: 1);

        HttpResponseMessage response = await alice.GetAsync($"/api/spaces/{spaceId}/access");
        JsonElement access = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        access.GetArrayLength().Should().Be(2);
        JsonElement aliceEntry = FindAccessEntry(access, AliceId);
        aliceEntry.GetProperty("permission").GetInt32().Should().Be((int)SpacePermission.Owner);
        aliceEntry.GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
        JsonElement bobEntry = FindAccessEntry(access, bobId);
        bobEntry.GetProperty("permission").GetInt32().Should().Be((int)SpacePermission.Read);
        bobEntry.GetProperty("displayName").GetString().Should().Be("Bob");
    }

    [TestMethod]
    public async Task AddingAnUnknownUserIsNotFound()
    {
        (_, JsonElement created) = await CreateSpaceAsync();
        Guid spaceId = created.GetProperty("id").GetGuid();

        HttpResponseMessage response = await AddAccessAsync(
            alice,
            spaceId,
            Guid.NewGuid(),
            SpacePermission.Read,
            version: 1);
        JsonElement problem = await ReadJsonAsync(response);
        HttpResponseMessage unchangedResponse = await alice.GetAsync($"/api/spaces/{spaceId}");
        JsonElement unchanged = await ReadJsonAsync(unchangedResponse);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        problem.GetProperty("title").GetString().Should().Be("Resource not found.");
        unchanged.GetProperty("version").GetInt64().Should().Be(1);
    }

    [TestMethod]
    public async Task TheLastOwnerCannotBeDowngradedOrRemoved()
    {
        (_, JsonElement created) = await CreateSpaceAsync();
        Guid spaceId = created.GetProperty("id").GetGuid();

        HttpResponseMessage downgradeResponse = await alice.PutAsJsonAsync(
            $"/api/spaces/{spaceId}/access/{AliceId}",
            new ChangeSpacePermissionRequest { Permission = SpacePermission.Read, Version = 1 });
        JsonElement downgradeProblem = await ReadJsonAsync(downgradeResponse);
        HttpResponseMessage removeResponse = await RemoveAccessAsync(alice, spaceId, AliceId, 1);
        JsonElement removeProblem = await ReadJsonAsync(removeResponse);
        HttpResponseMessage unchangedResponse = await alice.GetAsync($"/api/spaces/{spaceId}");
        JsonElement unchanged = await ReadJsonAsync(unchangedResponse);

        downgradeResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        downgradeProblem.GetProperty("title").GetString().Should().Be("Domain rule conflict.");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        removeProblem.GetProperty("title").GetString().Should().Be("Domain rule conflict.");
        unchanged.GetProperty("version").GetInt64().Should().Be(1);
        unchanged.GetProperty("permission").GetInt32().Should().Be((int)SpacePermission.Owner);
    }

    [TestMethod]
    public async Task PutRenamesTheSpaceAndAStaleVersionConflicts()
    {
        (_, JsonElement created) = await CreateSpaceAsync();
        Guid spaceId = created.GetProperty("id").GetGuid();

        HttpResponseMessage renameResponse = await alice.PutAsJsonAsync(
            $"/api/spaces/{spaceId}",
            new RenameSpaceRequest { Name = "Project Beta", Version = 1 });
        JsonElement renamed = await ReadJsonAsync(renameResponse);
        HttpResponseMessage staleResponse = await alice.PutAsJsonAsync(
            $"/api/spaces/{spaceId}",
            new RenameSpaceRequest { Name = "Project Gamma", Version = 1 });
        JsonElement problem = await ReadJsonAsync(staleResponse);
        HttpResponseMessage listResponse = await alice.GetAsync("/api/spaces");
        JsonElement list = await ReadJsonAsync(listResponse);

        renameResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        renamed.GetProperty("name").GetString().Should().Be("Project Beta");
        renamed.GetProperty("version").GetInt64().Should().Be(2);
        staleResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        problem.GetProperty("title").GetString().Should().Be("Concurrency conflict.");
        list.EnumerateArray()
            .Single(space => space.GetProperty("id").GetGuid() == spaceId)
            .GetProperty("name").GetString().Should().Be("Project Beta");
    }

    [TestMethod]
    public async Task AccessChangesFromTheSameVersionYieldOneSuccessAndOneConflict()
    {
        (_, JsonElement created) = await CreateSpaceAsync();
        Guid spaceId = created.GetProperty("id").GetGuid();
        Guid bobId = await SeedUserAsync("Bob");
        Guid carolId = await SeedUserAsync("Carol");

        HttpResponseMessage[] responses = await Task.WhenAll(
            AddAccessAsync(alice, spaceId, bobId, SpacePermission.Read, version: 1),
            AddAccessAsync(alice, spaceId, carolId, SpacePermission.Read, version: 1));
        HttpResponseMessage finalResponse = await alice.GetAsync($"/api/spaces/{spaceId}");
        JsonElement final = await ReadJsonAsync(finalResponse);

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
        HttpResponseMessage conflict = responses.Single(
            response => response.StatusCode == HttpStatusCode.Conflict);
        JsonElement problem = await ReadJsonAsync(conflict);
        problem.GetProperty("title").GetString().Should().Be("Concurrency conflict.");
        final.GetProperty("version").GetInt64().Should().Be(2);
        final.GetProperty("access").GetArrayLength().Should().Be(2);
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

    private static JsonElement FindAccessEntry(JsonElement spaceOrAccess, Guid subjectId)
    {
        JsonElement access = spaceOrAccess.ValueKind == JsonValueKind.Array
            ? spaceOrAccess
            : spaceOrAccess.GetProperty("access");

        return access.EnumerateArray()
            .Single(entry => entry.GetProperty("subjectId").GetGuid() == subjectId);
    }

    private static async Task<HttpResponseMessage> AddAccessAsync(
        HttpClient client,
        Guid spaceId,
        Guid subjectId,
        SpacePermission permission,
        long version)
    {
        return await client.PostAsJsonAsync(
            $"/api/spaces/{spaceId}/access",
            new AddSpaceAccessRequest
            {
                SubjectId = subjectId,
                Permission = permission,
                Version = version,
            });
    }

    private static async Task<HttpResponseMessage> RemoveAccessAsync(
        HttpClient client,
        Guid spaceId,
        Guid subjectId,
        long version)
    {
        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/spaces/{spaceId}/access/{subjectId}")
        {
            Content = JsonContent.Create(new RemoveSpaceAccessRequest { Version = version }),
        };

        return await client.SendAsync(request);
    }

    private async Task<(HttpResponseMessage Response, JsonElement Space)> CreateSpaceAsync(
        string name = "Project Alpha")
    {
        HttpResponseMessage response = await alice.PostAsJsonAsync(
            "/api/spaces",
            new CreateSpaceRequest { Name = name });
        JsonElement space = await ReadJsonAsync(response);

        return (response, space);
    }

    /// <summary>
    /// Registers a user in the directory the way a first sign-in would, and
    /// returns the internal identifier a client for that user authenticates
    /// as. The test authentication handler never populates the directory, and
    /// only a user the directory knows can be granted access to a Space.
    /// </summary>
    private async Task<Guid> SeedUserAsync(string displayName)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IUserDirectoryRepository users = scope.ServiceProvider
            .GetRequiredService<IUserDirectoryRepository>();
        UserIdentity identity = await users.ResolveAsync(
            Issuer,
            $"subject-{displayName.ToLowerInvariant()}",
            displayName,
            $"{displayName.ToLowerInvariant()}@sleeky.test");

        return identity.UserId;
    }
}
