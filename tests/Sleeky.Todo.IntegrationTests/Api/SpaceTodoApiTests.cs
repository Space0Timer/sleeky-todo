using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using MongoDB.Driver;

using Sleeky.Todo.Api.Contracts.Todos;
using Sleeky.Todo.Domain.Enums;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// What a Space is for: several people working on one collection, and nothing
/// reaching across the boundary between two of them.
/// </summary>
/// <remarks>
/// Four actors throughout — Alice owns the Space, Bob writes in it, Charlie
/// reads it, and Dave is not a member — because the interesting answers differ
/// per level and the 404-versus-403 split is the rule most easily got wrong.
/// </remarks>
[TestClass]
public sealed class SpaceTodoApiTests
{
    private static readonly Guid AliceId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid BobId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid CharlieId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid DaveId = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private static MongoDbContainer? mongoDbContainer;

    private HttpClient alice = null!;
    private HttpClient bob = null!;
    private HttpClient charlie = null!;
    private HttpClient dave = null!;
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

        databaseName = $"sleekyTodoSpaceApiTests_{Guid.NewGuid():N}";
        factory = new TodoApiFactory(
            mongoDbContainer.GetConnectionString(),
            databaseName);

        alice = await factory.CreateAuthenticatedClientAsync(AliceId);
        bob = await factory.CreateAuthenticatedClientAsync(BobId);
        charlie = await factory.CreateAuthenticatedClientAsync(CharlieId);
        dave = await factory.CreateAuthenticatedClientAsync(DaveId);

        spaceId = await factory.CreateSpaceAsync(AliceId, "Project Alpha");
        await factory.GrantAsync(spaceId, BobId, SpacePermission.Write);
        await factory.GrantAsync(spaceId, CharlieId, SpacePermission.Read);
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        alice?.Dispose();
        bob?.Dispose();
        charlie?.Dispose();
        dave?.Dispose();
        factory?.Dispose();

        if (mongoDbContainer is not null && databaseName is not null)
        {
            MongoClient mongoClient = new MongoClient(
                mongoDbContainer.GetConnectionString());
            await mongoClient.DropDatabaseAsync(databaseName);
        }
    }

    /// <summary>
    /// The requirement itself: one collection, several people. Each member's
    /// list shows the other's TODOs, and every item names the Space it lives in
    /// and the member who created it.
    /// </summary>
    [TestMethod]
    public async Task MembersSeeOneAnothersTodosInTheSharedSpace()
    {
        JsonElement aliceTodo = await CreateAsync(alice, "Alice's task");
        JsonElement bobTodo = await CreateAsync(bob, "Bob's task");

        JsonElement bobsList = await ListAsync(bob);
        JsonElement alicesList = await ListAsync(alice);

        aliceTodo.GetProperty("spaceId").GetGuid().Should().Be(spaceId);
        aliceTodo.GetProperty("createdByUserId").GetGuid().Should().Be(AliceId);
        bobTodo.GetProperty("spaceId").GetGuid().Should().Be(spaceId);
        bobTodo.GetProperty("createdByUserId").GetGuid().Should().Be(BobId);

        Names(bobsList).Should().BeEquivalentTo("Alice's task", "Bob's task");
        Names(alicesList).Should().BeEquivalentTo("Alice's task", "Bob's task");
        CreatorsByName(alicesList).Should().BeEquivalentTo(new Dictionary<string, Guid>
        {
            ["Alice's task"] = AliceId,
            ["Bob's task"] = BobId,
        });
        SpaceIds(alicesList).Should().AllBeEquivalentTo(spaceId);
    }

    /// <summary>
    /// A Read member sees everything and changes nothing. The refusal is 403,
    /// not 404: they can see the Space, so hiding it would be misleading.
    /// </summary>
    [TestMethod]
    public async Task AReadMemberCanReadEverythingAndWriteNothing()
    {
        JsonElement todo = await CreateAsync(alice, "Alice's task");
        Guid todoId = todo.GetProperty("id").GetGuid();

        using HttpResponseMessage list = await charlie.GetAsync(Todos);
        using HttpResponseMessage detail = await charlie.GetAsync($"{Todos}/{todoId}");
        using HttpResponseMessage selection = await charlie.GetAsync(
            $"{Todos}/selection?id={todoId}");

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        selection.StatusCode.Should().Be(HttpStatusCode.OK);

        foreach ((string Description, HttpResponseMessage Response) write in
            await AttemptEveryWriteAsync(charlie, todoId))
        {
            using HttpResponseMessage response = write.Response;
            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden,
                $"{write.Description} is a write");
        }

        JsonElement unchanged = await ReadAsync(alice, todoId);
        unchanged.GetProperty("version").GetInt64().Should().Be(1);
        unchanged.GetProperty("name").GetString().Should().Be("Alice's task");
    }

    /// <summary>
    /// A non-member is answered 404 on every route under the Space, reads
    /// included, so a probe cannot tell an unknown Space from one they simply
    /// have no part in.
    /// </summary>
    [TestMethod]
    public async Task ANonMemberIsAnsweredNotFoundOnEveryRoute()
    {
        JsonElement todo = await CreateAsync(alice, "Alice's task");
        Guid todoId = todo.GetProperty("id").GetGuid();

        using HttpResponseMessage list = await dave.GetAsync(Todos);
        using HttpResponseMessage detail = await dave.GetAsync($"{Todos}/{todoId}");
        using HttpResponseMessage selection = await dave.GetAsync(
            $"{Todos}/selection?id={todoId}");

        list.StatusCode.Should().Be(HttpStatusCode.NotFound);
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        selection.StatusCode.Should().Be(HttpStatusCode.NotFound);

        foreach ((string Description, HttpResponseMessage Response) write in
            await AttemptEveryWriteAsync(dave, todoId))
        {
            using HttpResponseMessage response = write.Response;
            response.StatusCode.Should().Be(
                HttpStatusCode.NotFound,
                $"{write.Description} names a Space Dave cannot see");
        }

        JsonElement unchanged = await ReadAsync(alice, todoId);
        unchanged.GetProperty("version").GetInt64().Should().Be(1);
    }

    /// <summary>
    /// The Space in the route is what the TODO is looked up under, so a member
    /// of both Spaces still cannot reach A's TODO through B's route.
    /// </summary>
    [TestMethod]
    public async Task ATodoIsUnreachableThroughAnotherSpacesRoute()
    {
        Guid otherSpaceId = await factory.CreateSpaceAsync(AliceId, "Project Beta");
        string otherRoute = $"/api/spaces/{otherSpaceId}/todos";
        JsonElement todo = await CreateAsync(alice, "Alice's task");
        Guid todoId = todo.GetProperty("id").GetGuid();

        using HttpResponseMessage read = await alice.GetAsync($"{otherRoute}/{todoId}");
        using HttpResponseMessage updated = await alice.PutAsJsonAsync(
            $"{otherRoute}/{todoId}",
            UpdateRequest("Renamed from another Space"));
        using HttpResponseMessage deleted = await alice.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"{otherRoute}/{todoId}")
            {
                Content = JsonContent.Create(new { version = 1 }),
            });

        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        updated.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deleted.StatusCode.Should().Be(HttpStatusCode.NotFound);

        JsonElement unchanged = await ReadAsync(alice, todoId);
        unchanged.GetProperty("name").GetString().Should().Be("Alice's task");
        unchanged.GetProperty("version").GetInt64().Should().Be(1);
        unchanged.GetProperty("deletedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Sharing a Space does not serialize its members. Concurrency is per TODO,
    /// as it was before Spaces existed, so two people editing different items
    /// both succeed.
    /// </summary>
    [TestMethod]
    public async Task TwoMembersEditingDifferentTodosBothSucceed()
    {
        Guid first = (await CreateAsync(alice, "First")).GetProperty("id").GetGuid();
        Guid second = (await CreateAsync(bob, "Second")).GetProperty("id").GetGuid();

        HttpResponseMessage[] responses = await Task.WhenAll(
            alice.PutAsJsonAsync($"{Todos}/{first}", UpdateRequest("First, edited")),
            bob.PutAsJsonAsync($"{Todos}/{second}", UpdateRequest("Second, edited")));

        try
        {
            responses.Select(response => response.StatusCode)
                .Should().AllBeEquivalentTo(HttpStatusCode.OK);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>
    /// The same TODO is still one concurrency boundary: two members holding the
    /// same version race, one wins, and the loser is told to re-read rather
    /// than having their change silently overwrite the winner's.
    /// </summary>
    [TestMethod]
    public async Task TwoMembersEditingTheSameTodoLeaveOneConflict()
    {
        Guid todoId = (await CreateAsync(alice, "Shared")).GetProperty("id").GetGuid();

        HttpResponseMessage[] responses = await Task.WhenAll(
            alice.PutAsJsonAsync($"{Todos}/{todoId}", UpdateRequest("Alice's edit")),
            bob.PutAsJsonAsync($"{Todos}/{todoId}", UpdateRequest("Bob's edit")));

        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.OK)
                .Should().Be(1);
            responses.Count(response => response.StatusCode == HttpStatusCode.Conflict)
                .Should().Be(1);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        (await ReadAsync(alice, todoId)).GetProperty("version").GetInt64().Should().Be(2);
    }

    /// <summary>
    /// A batch is all or nothing, and the Space filter is what makes a foreign
    /// identifier unresolvable: the batch loader reads through the same filter
    /// every other query does, so the whole request fails as a missing TODO
    /// before a single write is composed, and the Space's own TODOs are
    /// untouched.
    /// </summary>
    [TestMethod]
    public async Task ABulkStatusChangeNamingAnotherSpacesTodoChangesNothing()
    {
        Guid otherSpaceId = await factory.CreateSpaceAsync(AliceId, "Project Beta");
        Guid mine = (await CreateAsync(alice, "Mine")).GetProperty("id").GetGuid();
        Guid foreign = (await CreateAsync(alice, "Theirs", otherSpaceId))
            .GetProperty("id").GetGuid();

        using HttpResponseMessage response = await alice.PutAsJsonAsync(
            $"{Todos}/status",
            new BulkChangeTodoStatusRequest
            {
                Status = TodoStatus.Completed,
                Items =
                [
                    new BulkTodoSelectionItem { Id = mine, Version = 1 },
                    new BulkTodoSelectionItem { Id = foreign, Version = 1 },
                ],
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAsync(alice, mine)).GetProperty("status").GetInt32()
            .Should().Be((int)TodoStatus.Open);
        (await ReadAsync(alice, foreign, otherSpaceId)).GetProperty("status").GetInt32()
            .Should().Be((int)TodoStatus.Open);
    }

    /// <summary>
    /// A dependency is looked up through the same Space filter, so an edge
    /// across the boundary reports the other end as missing rather than
    /// building a graph that spans two collections.
    /// </summary>
    [TestMethod]
    public async Task ADependencyOnAnotherSpacesTodoIsNotFound()
    {
        Guid otherSpaceId = await factory.CreateSpaceAsync(AliceId, "Project Beta");
        Guid dependent = (await CreateAsync(alice, "Dependent")).GetProperty("id").GetGuid();
        Guid foreign = (await CreateAsync(alice, "Prerequisite", otherSpaceId))
            .GetProperty("id").GetGuid();

        using HttpResponseMessage response = await alice.PostAsJsonAsync(
            $"{Todos}/{dependent}/dependencies",
            new AddDependencyRequest { DependencyId = foreign, Version = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAsync(alice, dependent)).GetProperty("dependencyIds")
            .GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// A cursor is signed over the Space it was minted in, so replaying one in
    /// another Space is a malformed continuation rather than a page of the
    /// wrong collection.
    /// </summary>
    [TestMethod]
    public async Task ACursorFromOneSpaceIsRejectedInAnother()
    {
        Guid otherSpaceId = await factory.CreateSpaceAsync(AliceId, "Project Beta");
        _ = await CreateAsync(alice, "First");
        _ = await CreateAsync(alice, "Second");

        JsonElement firstPage = await ReadJsonAsync(
            await alice.GetAsync($"{Todos}?limit=1"));
        string cursor = firstPage.GetProperty("nextCursor").GetString()
            ?? throw new InvalidOperationException("A second page was expected.");

        using HttpResponseMessage replayed = await alice.GetAsync(
            $"/api/spaces/{otherSpaceId}/todos?limit=1&cursor={Uri.EscapeDataString(cursor)}");

        replayed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A recurring series belongs to the Space, and to whoever started it. Bob
    /// completing Alice's occurrence produces a successor still in the Space
    /// and still attributed to Alice — completing a step is not authorship.
    /// </summary>
    [TestMethod]
    public async Task ASuccessorKeepsTheSpaceAndTheOriginalCreator()
    {
        JsonElement recurring = await CreateAsync(
            alice,
            "Monthly report",
            recurrence: new RecurrenceRequest
            {
                Type = RecurrenceType.Monthly,
                Interval = 1,
            });
        Guid recurringId = recurring.GetProperty("id").GetGuid();

        using HttpResponseMessage completion = await bob.PutAsJsonAsync(
            $"{Todos}/{recurringId}/status",
            new ChangeTodoStatusRequest { Status = TodoStatus.Completed, Version = 1 });
        JsonElement completed = await ReadJsonAsync(completion);
        Guid successorId = completed.GetProperty("nextOccurrenceId").GetGuid();

        JsonElement successor = await ReadAsync(alice, successorId);

        completion.StatusCode.Should().Be(HttpStatusCode.OK);
        successor.GetProperty("spaceId").GetGuid().Should().Be(spaceId);
        successor.GetProperty("createdByUserId").GetGuid().Should().Be(AliceId);
        successor.GetProperty("occurrenceNumber").GetInt32().Should().Be(2);
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static UpdateTodoRequest UpdateRequest(string name)
    {
        return new UpdateTodoRequest
        {
            Name = name,
            Description = null,
            DueDate = new DateOnly(2026, 9, 30),
            Priority = TodoPriority.Medium,
            Version = 1,
        };
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return JsonDocument
                .Parse(await response.Content.ReadAsStringAsync())
                .RootElement;
        }
    }

    private static IEnumerable<string> Names(JsonElement page)
    {
        return page.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!);
    }

    private static IEnumerable<Guid> SpaceIds(JsonElement page)
    {
        return page.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("spaceId").GetGuid());
    }

    private static Dictionary<string, Guid> CreatorsByName(JsonElement page)
    {
        return page.GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("name").GetString()!,
                item => item.GetProperty("createdByUserId").GetGuid());
    }

    /// <summary>
    /// One attempt at each of the thirteen routes that write, so a permission
    /// scenario asserts the whole surface rather than the two endpoints that
    /// happened to come to mind.
    /// </summary>
    private async Task<IReadOnlyList<(string Description, HttpResponseMessage Response)>>
        AttemptEveryWriteAsync(HttpClient member, Guid todoId)
    {
        BulkTodoSelectionItem[] selection =
        [
            new BulkTodoSelectionItem { Id = todoId, Version = 1 },
        ];

        return
        [
            ("create", await member.PostAsJsonAsync(
                Todos,
                new CreateTodoRequest
                {
                    Name = "Should not exist",
                    DueDate = new DateOnly(2026, 9, 30),
                    Priority = TodoPriority.Medium,
                })),
            ("update", await member.PutAsJsonAsync(
                $"{Todos}/{todoId}",
                UpdateRequest("Should not apply"))),
            ("status", await member.PutAsJsonAsync(
                $"{Todos}/{todoId}/status",
                new ChangeTodoStatusRequest
                {
                    Status = TodoStatus.Completed,
                    Version = 1,
                })),
            ("add dependency", await member.PostAsJsonAsync(
                $"{Todos}/{todoId}/dependencies",
                new AddDependencyRequest { DependencyId = Guid.NewGuid(), Version = 1 })),
            ("remove dependency", await member.SendAsync(
                new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"{Todos}/{todoId}/dependencies/{Guid.NewGuid()}")
                {
                    Content = JsonContent.Create(new { version = 1 }),
                })),
            ("delete", await member.SendAsync(
                new HttpRequestMessage(HttpMethod.Delete, $"{Todos}/{todoId}")
                {
                    Content = JsonContent.Create(new { version = 1 }),
                })),
            ("restore", await member.PostAsJsonAsync(
                $"{Todos}/{todoId}/restore",
                new RestoreTodoRequest { Version = 1 })),
            ("bulk status", await member.PutAsJsonAsync(
                $"{Todos}/status",
                new BulkChangeTodoStatusRequest
                {
                    Status = TodoStatus.Completed,
                    Items = selection,
                })),
            ("bulk restore", await member.PostAsJsonAsync(
                $"{Todos}/restore",
                new BulkRestoreTodosRequest { Items = selection })),
            ("bulk delete", await member.SendAsync(
                new HttpRequestMessage(HttpMethod.Delete, Todos)
                {
                    Content = JsonContent.Create(
                        new BulkDeleteTodosRequest { Items = selection }),
                })),
        ];
    }

    private async Task<JsonElement> CreateAsync(
        HttpClient member,
        string name,
        Guid? inSpaceId = null,
        RecurrenceRequest? recurrence = null)
    {
        HttpResponseMessage response = await member.PostAsJsonAsync(
            $"/api/spaces/{inSpaceId ?? spaceId}/todos",
            new CreateTodoRequest
            {
                Name = name,
                DueDate = new DateOnly(2026, 9, 30),
                Priority = TodoPriority.Medium,
                Recurrence = recurrence,
            });
        response.EnsureSuccessStatusCode();

        return await ReadJsonAsync(response);
    }

    private async Task<JsonElement> ReadAsync(
        HttpClient member,
        Guid todoId,
        Guid? inSpaceId = null)
    {
        HttpResponseMessage response = await member.GetAsync(
            $"/api/spaces/{inSpaceId ?? spaceId}/todos/{todoId}");
        response.EnsureSuccessStatusCode();

        return await ReadJsonAsync(response);
    }

    private async Task<JsonElement> ListAsync(HttpClient member)
    {
        HttpResponseMessage response = await member.GetAsync(Todos);
        response.EnsureSuccessStatusCode();

        return await ReadJsonAsync(response);
    }
}
