using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Api.Contracts.Todos;
using Sleeky.Todo.Domain.Enums;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Api;

[TestClass]
public sealed class BulkTodoApiTests
{
    private static readonly Guid UserId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static MongoDbContainer? mongoDbContainer;

    private HttpClient client = null!;
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

        databaseName = $"sleekyTodoBulkApiTests_{Guid.NewGuid():N}";
        factory = new TodoApiFactory(
            mongoDbContainer.GetConnectionString(),
            databaseName);
        client = await factory.CreateAuthenticatedClientAsync(UserId);
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        client?.Dispose();
        factory?.Dispose();

        if (mongoDbContainer is not null && databaseName is not null)
        {
            MongoClient mongoClient = new MongoClient(
                mongoDbContainer.GetConnectionString());
            await mongoClient.DropDatabaseAsync(databaseName);
        }
    }

    [TestMethod]
    public async Task BulkCompleteAdvancesEverySelectedTodo()
    {
        JsonElement first = await CreateTodoAsync();
        JsonElement second = await CreateTodoAsync();

        HttpResponseMessage response = await ChangeStatusesAsync(
            TodoStatus.Completed,
            Select(first, second));
        JsonElement body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement[] items = body.GetProperty("items").EnumerateArray().ToArray();
        items.Should().HaveCount(2);
        items.Should().OnlyContain(item =>
            item.GetProperty("status").GetInt32() == (int)TodoStatus.Completed);
        items.Should().OnlyContain(item => item.GetProperty("version").GetInt64() == 2);
    }

    [TestMethod]
    public async Task BulkCompleteAcceptsAPrerequisiteAndItsDependentTogether()
    {
        JsonElement prerequisite = await CreateTodoAsync();
        JsonElement dependent = await CreateTodoAsync();
        JsonElement linked = await AddDependencyAsync(dependent, prerequisite);

        HttpResponseMessage response = await ChangeStatusesAsync(
            TodoStatus.Completed,
            Select(prerequisite),
            Selection(linked));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("items").EnumerateArray().Should().OnlyContain(item =>
            item.GetProperty("status").GetInt32() == (int)TodoStatus.Completed);
    }

    [TestMethod]
    public async Task BulkCompleteRejectsATodoBlockedByAnUnselectedDependency()
    {
        JsonElement prerequisite = await CreateTodoAsync();
        JsonElement dependent = await CreateTodoAsync();
        JsonElement linked = await AddDependencyAsync(dependent, prerequisite);

        HttpResponseMessage response = await ChangeStatusesAsync(
            TodoStatus.Completed,
            [Selection(linked)]);
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        problem.GetProperty("detail").GetString().Should()
            .Be("A blocked TODO cannot move to Completed.");
        (await GetTodoAsync(GetId(linked))).GetProperty("status").GetInt32()
            .Should().Be((int)TodoStatus.Open);
    }

    [TestMethod]
    public async Task BulkArchiveIgnoresDependencies()
    {
        JsonElement prerequisite = await CreateTodoAsync();
        JsonElement dependent = await CreateTodoAsync();
        JsonElement linked = await AddDependencyAsync(dependent, prerequisite);

        HttpResponseMessage response = await ChangeStatusesAsync(
            TodoStatus.Archived,
            [Selection(linked)]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetTodoAsync(GetId(linked))).GetProperty("status").GetInt32()
            .Should().Be((int)TodoStatus.Archived);
    }

    [TestMethod]
    public async Task AStaleVersionRollsBackTheWholeBatch()
    {
        JsonElement first = await CreateTodoAsync();
        JsonElement second = await CreateTodoAsync();

        HttpResponseMessage response = await ChangeStatusesAsync(
            TodoStatus.Completed,
            Select(first),
            new BulkTodoSelectionItem { Id = Guid.Parse(GetId(second)), Version = 9 });
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        problem.GetProperty("detail").GetString().Should().Contain(GetId(second));
        (await GetTodoAsync(GetId(first))).GetProperty("status").GetInt32()
            .Should().Be((int)TodoStatus.Open);
    }

    [TestMethod]
    public async Task BulkCompleteCreatesOneNextOccurrencePerRecurringTodo()
    {
        JsonElement first = await CreateTodoAsync(new RecurrenceRequest
        {
            Type = RecurrenceType.Monthly,
            Interval = 1,
        });
        JsonElement second = await CreateTodoAsync(new RecurrenceRequest
        {
            Type = RecurrenceType.Daily,
            Interval = 1,
        });

        HttpResponseMessage response = await ChangeStatusesAsync(
            TodoStatus.Completed,
            Select(first, second));
        JsonElement body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Guid[] nextOccurrenceIds = body.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("nextOccurrenceId").GetGuid())
            .ToArray();
        nextOccurrenceIds.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        foreach (Guid nextOccurrenceId in nextOccurrenceIds)
        {
            (await GetTodoAsync(nextOccurrenceId.ToString()))
                .GetProperty("occurrenceNumber").GetInt32().Should().Be(2);
        }
    }

    /// <summary>
    /// Re-completing a reopened occurrence would insert occurrence two a second
    /// time. The unique series index rejects it, and the driver reports that as
    /// a bulk write error rather than the single write error the transaction
    /// executor classifies, so the repository has to translate it.
    /// </summary>
    [TestMethod]
    public async Task ADuplicateRecurringOccurrenceRollsBackWithAConflict()
    {
        JsonElement recurring = await CreateTodoAsync(new RecurrenceRequest
        {
            Type = RecurrenceType.Monthly,
            Interval = 1,
        });
        string recurringId = GetId(recurring);
        (await ChangeStatusesAsync(TodoStatus.Completed, Select(recurring)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await ReopenAsync(recurringId, 2);

        HttpResponseMessage response = await ChangeStatusesAsync(
            TodoStatus.Completed,
            [new BulkTodoSelectionItem { Id = Guid.Parse(recurringId), Version = 3 }]);
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        problem.GetProperty("title").GetString().Should().Be("Concurrency conflict.");
        problem.GetProperty("detail").GetString().Should()
            .MatchRegex("'[0-9a-fA-F-]{36}'");
        (await GetTodoAsync(recurringId)).GetProperty("status").GetInt32()
            .Should().Be((int)TodoStatus.Open);
        (await CountTodoDocumentsAsync()).Should().Be(2);
    }

    [TestMethod]
    public async Task BulkDeleteRemovesEverySelectedTodo()
    {
        JsonElement first = await CreateTodoAsync();
        JsonElement second = await CreateTodoAsync();

        HttpResponseMessage response = await DeleteManyAsync(Select(first, second));
        JsonElement body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("items").EnumerateArray().Should().OnlyContain(item =>
            item.GetProperty("deletedAt").ValueKind != JsonValueKind.Null);
        (await client.GetAsync($"/api/todos/{GetId(first)}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task BulkDeleteAcceptsADependentAndItsPrerequisiteTogether()
    {
        JsonElement prerequisite = await CreateTodoAsync();
        JsonElement dependent = await CreateTodoAsync();
        JsonElement linked = await AddDependencyAsync(dependent, prerequisite);

        HttpResponseMessage response = await DeleteManyAsync(
            Select(prerequisite),
            Selection(linked));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task BulkDeleteRejectsAPrerequisiteWithAnUnselectedDependent()
    {
        JsonElement prerequisite = await CreateTodoAsync();
        JsonElement dependent = await CreateTodoAsync();
        _ = await AddDependencyAsync(dependent, prerequisite);

        HttpResponseMessage response = await DeleteManyAsync(Select(prerequisite));
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        problem.GetProperty("detail").GetString().Should()
            .Be("A TODO with active dependents cannot be deleted.");
        (await client.GetAsync($"/api/todos/{GetId(prerequisite)}")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task AMissingTodoInTheSelectionIsNotFound()
    {
        JsonElement present = await CreateTodoAsync();

        HttpResponseMessage response = await ChangeStatusesAsync(
            TodoStatus.Completed,
            Select(present),
            new BulkTodoSelectionItem { Id = Guid.NewGuid(), Version = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task ASelectionAboveTheLimitIsRejected()
    {
        BulkTodoSelectionItem[] items = Enumerable.Range(0, 101)
            .Select(_ => new BulkTodoSelectionItem { Id = Guid.NewGuid(), Version = 1 })
            .ToArray();

        HttpResponseMessage response = await ChangeStatusesAsync(
            TodoStatus.Completed,
            items);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task AnUnknownBulkStatusIsRejected()
    {
        JsonElement todo = await CreateTodoAsync();

        HttpResponseMessage response = await ChangeStatusesAsync(
            (TodoStatus)99,
            Select(todo));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task ArchivedTodosAreUnarchivedInBulk()
    {
        JsonElement todo = await CreateTodoAsync();
        HttpResponseMessage archiveResponse = await ChangeStatusesAsync(
            TodoStatus.Archived,
            Select(todo));
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage response = await ChangeStatusesAsync(
            TodoStatus.Open,
            [new BulkTodoSelectionItem { Id = Guid.Parse(GetId(todo)), Version = 2 }]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await ReadJsonAsync(response);
        JsonElement item = body.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("status").GetInt32().Should().Be((int)TodoStatus.Open);
        item.GetProperty("version").GetInt64().Should().Be(3);
    }

    [TestMethod]
    public async Task TheSelectionQueryAnswersInRequestOrder()
    {
        JsonElement first = await CreateTodoAsync();
        JsonElement second = await CreateTodoAsync();
        JsonElement third = await CreateTodoAsync();
        Guid[] requested = [Id(third), Id(first), Id(second)];

        HttpResponseMessage response = await GetSelectionAsync(requested);
        JsonElement body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        SelectedIds(body).Should().Equal(requested);
    }

    [TestMethod]
    public async Task AnUnknownIdIsOmittedFromTheSelectionRatherThanFailingIt()
    {
        JsonElement first = await CreateTodoAsync();
        JsonElement second = await CreateTodoAsync();

        HttpResponseMessage response = await GetSelectionAsync(
            Id(first),
            Guid.NewGuid(),
            Id(second));
        JsonElement body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        SelectedIds(body).Should().Equal(Id(first), Id(second));
    }

    /// <summary>
    /// A soft-deleted TODO still resolves. The trash lists it and a selection
    /// there is restorable, so a client repairing that selection has to be able
    /// to read it; only what is purged or owned by someone else is absent.
    /// </summary>
    [TestMethod]
    public async Task ADeletedTodoIsStillReportedBySelectionWithItsDeletion()
    {
        JsonElement survivor = await CreateTodoAsync();
        JsonElement removed = await CreateTodoAsync();
        (await DeleteManyAsync(Select(removed))).StatusCode
            .Should().Be(HttpStatusCode.OK);

        HttpResponseMessage response = await GetSelectionAsync(
            Id(removed),
            Id(survivor));
        JsonElement body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        SelectedIds(body).Should().Equal(Id(removed), Id(survivor));
        body.GetProperty("items").EnumerateArray().First()
            .GetProperty("deletedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [TestMethod]
    public async Task ASelectionQueryAboveTheLimitIsRejected()
    {
        Guid[] ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();

        HttpResponseMessage response = await GetSelectionAsync(ids);
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ValidationMessages(problem).Should()
            .Contain("No more than 100 TODOs can be selected.");
    }

    [TestMethod]
    public async Task ARepeatedIdInTheSelectionQueryIsRejected()
    {
        JsonElement todo = await CreateTodoAsync();

        HttpResponseMessage response = await GetSelectionAsync(Id(todo), Id(todo));
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ValidationMessages(problem).Should()
            .Contain("A TODO can only be selected once.");
    }

    [TestMethod]
    public async Task BulkRestoreReturnsEveryDeletedTodoToTheActiveList()
    {
        JsonElement first = await CreateTodoAsync();
        JsonElement second = await CreateTodoAsync();
        (await DeleteManyAsync(Select(first, second))).StatusCode
            .Should().Be(HttpStatusCode.OK);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/todos/restore",
            new BulkRestoreTodosRequest
            {
                Items =
                [
                    new BulkTodoSelectionItem { Id = Id(first), Version = 2 },
                    new BulkTodoSelectionItem { Id = Id(second), Version = 2 },
                ],
            });
        JsonElement body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        body.GetProperty("items").EnumerateArray().Should()
            .OnlyContain(item => item.GetProperty("deletedAt").ValueKind == JsonValueKind.Null);
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
        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(payload);

        return document.RootElement.Clone();
    }

    private static string GetId(JsonElement todo)
    {
        return todo.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("The response carried no TODO id.");
    }

    private static Guid Id(JsonElement todo)
    {
        return Guid.Parse(GetId(todo));
    }

    private static string[] ValidationMessages(JsonElement problem)
    {
        return problem.GetProperty("errors")
            .EnumerateObject()
            .SelectMany(error => error.Value.EnumerateArray())
            .Select(message => message.GetString() ?? string.Empty)
            .ToArray();
    }

    private static Guid[] SelectedIds(JsonElement selection)
    {
        return selection.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToArray();
    }

    private static BulkTodoSelectionItem Selection(JsonElement todo)
    {
        return new BulkTodoSelectionItem
        {
            Id = Guid.Parse(GetId(todo)),
            Version = todo.GetProperty("version").GetInt64(),
        };
    }

    private static BulkTodoSelectionItem[] Select(params JsonElement[] todos)
    {
        return todos.Select(Selection).ToArray();
    }

    private async Task<long> CountTodoDocumentsAsync()
    {
        MongoClient mongoClient = new MongoClient(
            mongoDbContainer!.GetConnectionString());

        return await mongoClient
            .GetDatabase(databaseName)
            .GetCollection<BsonDocument>("todoItems")
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
    }

    private async Task<JsonElement> CreateTodoAsync(RecurrenceRequest? recurrence = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/todos",
            new CreateTodoRequest
            {
                Name = "Submit report",
                Description = "Monthly report",
                DueDate = new DateOnly(2026, 8, 31),
                Priority = TodoPriority.High,
                Recurrence = recurrence,
            });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return await ReadJsonAsync(response);
    }

    private async Task<JsonElement> GetTodoAsync(string id)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/todos/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await ReadJsonAsync(response);
    }

    private Task<HttpResponseMessage> GetSelectionAsync(params Guid[] ids)
    {
        string query = string.Join('&', ids.Select(id => $"id={id}"));

        return client.GetAsync($"/api/todos/selection?{query}");
    }

    private async Task<JsonElement> AddDependencyAsync(
        JsonElement todo,
        JsonElement dependency)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/todos/{GetId(todo)}/dependencies",
            new AddDependencyRequest
            {
                DependencyId = Guid.Parse(GetId(dependency)),
                Version = todo.GetProperty("version").GetInt64(),
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await ReadJsonAsync(response);
    }

    private async Task ReopenAsync(string id, long version)
    {
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/todos/{id}/status",
            new ChangeTodoStatusRequest
            {
                Status = TodoStatus.Open,
                Version = version,
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> ChangeStatusesAsync(
        TodoStatus status,
        BulkTodoSelectionItem[] selection,
        params BulkTodoSelectionItem[] extra)
    {
        return client.PutAsJsonAsync(
            "/api/todos/status",
            new BulkChangeTodoStatusRequest
            {
                Status = status,
                Items = selection.Concat(extra).ToArray(),
            });
    }

    private async Task<HttpResponseMessage> DeleteManyAsync(
        BulkTodoSelectionItem[] selection,
        params BulkTodoSelectionItem[] extra)
    {
        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/todos")
        {
            Content = JsonContent.Create(new BulkDeleteTodosRequest
            {
                Items = selection.Concat(extra).ToArray(),
            }),
        };

        return await client.SendAsync(request);
    }
}
