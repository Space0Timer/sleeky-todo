using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Api.Contracts.Todos;
using Sleeky.Todo.Domain.Enums;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Api;

[TestClass]
public sealed class TodoApiTests
{
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
                "Set RUN_MONGODB_INTEGRATION_TESTS=true and start Docker to run API integration tests.");
        }

        databaseName = $"sleekyTodoApiTests_{Guid.NewGuid():N}";
        factory = new TodoApiFactory(
            mongoDbContainer.GetConnectionString(),
            databaseName);
        client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
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
    public async Task PostThenGetReturnsPersistedTodo()
    {
        (HttpResponseMessage createResponse, JsonElement createdTodo) =
            await CreateTodoAsync();
        string id = GetTodoId(createdTodo);

        HttpResponseMessage getResponse = await client.GetAsync($"/api/todos/{id}");
        JsonElement retrievedTodo = await ReadJsonAsync(getResponse);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        createResponse.Headers.Location.Should().NotBeNull();
        createResponse.Headers.Location!.ToString().Should().EndWith($"/api/todos/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        retrievedTodo.GetProperty("id").GetString().Should().Be(id);
        retrievedTodo.GetProperty("name").GetString().Should().Be("Submit report");
        retrievedTodo.GetProperty("version").GetInt64().Should().Be(1);
    }

    [TestMethod]
    public async Task PutIncrementsVersion()
    {
        (_, JsonElement createdTodo) = await CreateTodoAsync();
        string id = GetTodoId(createdTodo);
        UpdateTodoRequest request = new UpdateTodoRequest
        {
            Name = "Review report",
            Description = "Review the final draft",
            DueDate = new DateOnly(2026, 9, 1),
            Priority = TodoPriority.Medium,
            Version = 1,
        };

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/todos/{id}",
            request);
        JsonElement updatedTodo = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updatedTodo.GetProperty("name").GetString().Should().Be("Review report");
        updatedTodo.GetProperty("version").GetInt64().Should().Be(2);
    }

    [TestMethod]
    public async Task ConcurrentWritesWithSameVersionReturnOneSuccessAndOneConflict()
    {
        (_, JsonElement createdTodo) = await CreateTodoAsync();
        string id = GetTodoId(createdTodo);
        UpdateTodoRequest firstRequest = CreateUpdateRequest("First writer");
        UpdateTodoRequest secondRequest = CreateUpdateRequest("Second writer");

        HttpResponseMessage[] responses = await Task.WhenAll(
            client.PutAsJsonAsync($"/api/todos/{id}", firstRequest),
            client.PutAsJsonAsync($"/api/todos/{id}", secondRequest));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK)
            .Should().Be(1);
        HttpResponseMessage conflict = responses.Single(
            response => response.StatusCode == HttpStatusCode.Conflict);
        JsonElement problem = await ReadJsonAsync(conflict);
        problem.GetProperty("title").GetString().Should().Be("Concurrency conflict.");
        problem.GetProperty("status").GetInt32().Should().Be(409);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task DeleteHidesTodoFromNormalRetrieval()
    {
        (_, JsonElement createdTodo) = await CreateTodoAsync();
        string id = GetTodoId(createdTodo);

        HttpResponseMessage deleteResponse = await DeleteTodoAsync(id, version: 1);
        HttpResponseMessage getResponse = await client.GetAsync($"/api/todos/{id}");
        JsonElement problem = await ReadJsonAsync(getResponse);

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        problem.GetProperty("title").GetString().Should().Be("Resource not found.");
    }

    [TestMethod]
    public async Task RestoreMakesDeletedTodoVisibleAgain()
    {
        (_, JsonElement createdTodo) = await CreateTodoAsync();
        string id = GetTodoId(createdTodo);
        _ = await DeleteTodoAsync(id, version: 1);

        HttpResponseMessage restoreResponse = await client.PostAsJsonAsync(
            $"/api/todos/{id}/restore",
            new RestoreTodoRequest { Version = 2 });
        JsonElement restoredTodo = await ReadJsonAsync(restoreResponse);
        HttpResponseMessage getResponse = await client.GetAsync($"/api/todos/{id}");

        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        restoredTodo.GetProperty("version").GetInt64().Should().Be(3);
        restoredTodo.GetProperty("deletedAt").ValueKind.Should().Be(JsonValueKind.Null);
        restoredTodo.GetProperty("purgeAt").ValueKind.Should().Be(JsonValueKind.Null);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task InvalidRequestReturnsPredictableProblemDetails()
    {
        CreateTodoRequest request = new CreateTodoRequest
        {
            Name = "   ",
            DueDate = default,
            Priority = (TodoPriority)999,
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/todos",
            request);
        JsonElement problem = await ReadJsonAsync(response);
        JsonElement errors = problem.GetProperty("errors");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("title").GetString().Should().Be("Validation failed.");
        problem.GetProperty("status").GetInt32().Should().Be(400);
        problem.GetProperty("detail").GetString()
            .Should().Be("One or more validation errors occurred.");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        errors.TryGetProperty("name", out _).Should().BeTrue();
        errors.TryGetProperty("dueDate", out _).Should().BeTrue();
        errors.TryGetProperty("priority", out _).Should().BeTrue();
    }

    [TestMethod]
    public async Task MalformedDateReturnsModelBindingProblemDetails()
    {
        const string Json = """
            {
              "name": "Submit report",
              "dueDate": "not-a-date",
              "priority": 2
            }
            """;
        using StringContent content = new StringContent(
            Json,
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response = await client.PostAsync("/api/todos", content);
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("title").GetString().Should().Be("Validation failed.");
        problem.GetProperty("errors").EnumerateObject().Should().NotBeEmpty();
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task DomainRuleFailureReturnsConflictProblemDetails()
    {
        (_, JsonElement createdTodo) = await CreateTodoAsync();
        string id = GetTodoId(createdTodo);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/todos/{id}/restore",
            new RestoreTodoRequest { Version = 1 });
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        problem.GetProperty("title").GetString().Should().Be("Domain rule conflict.");
        problem.GetProperty("status").GetInt32().Should().Be(409);
        problem.GetProperty("detail").GetString()
            .Should().Be("Only a deleted TODO can be restored.");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task SwaggerGenerationDocumentsExpectedResponses()
    {
        HttpResponseMessage response = await client.GetAsync("/swagger/v1/swagger.json");
        JsonElement swagger = await ReadJsonAsync(response);
        JsonElement paths = swagger.GetProperty("paths");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertResponses(paths, "/api/todos", "post", "201", "400", "409");
        AssertResponses(paths, "/api/todos/{id}", "get", "200", "400", "404");
        AssertResponses(paths, "/api/todos/{id}", "put", "200", "400", "404", "409");
        AssertResponses(paths, "/api/todos/{id}", "delete", "204", "400", "404", "409");
        AssertResponses(paths, "/api/todos/{id}/restore", "post", "200", "400", "404", "409");
    }

    [TestMethod]
    public async Task HealthEndpointReportsMongoDbHealthy()
    {
        HttpResponseMessage response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task StartupInitializesMongoDbIndexes()
    {
        _ = await client.GetAsync("/health");
        MongoClient mongoClient = new MongoClient(
            mongoDbContainer!.GetConnectionString());
        IMongoCollection<BsonDocument> collection = mongoClient
            .GetDatabase(databaseName)
            .GetCollection<BsonDocument>("todoItems");

        using IAsyncCursor<BsonDocument> cursor = await collection.Indexes.ListAsync();
        List<BsonDocument> indexes = await cursor.ToListAsync();
        string[] indexNames = indexes
            .Select(index => index["name"].AsString)
            .ToArray();

        indexNames.Should().Contain("active_due_date_id");
        indexNames.Should().Contain("purge_at");
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

    private static UpdateTodoRequest CreateUpdateRequest(string name)
    {
        return new UpdateTodoRequest
        {
            Name = name,
            Description = null,
            DueDate = new DateOnly(2026, 8, 31),
            Priority = TodoPriority.High,
            Version = 1,
        };
    }

    private static string GetTodoId(JsonElement todo)
    {
        return todo.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("The API response should contain a TODO identifier.");
    }

    private static void AssertResponses(
        JsonElement paths,
        string path,
        string operation,
        params string[] expectedStatuses)
    {
        JsonElement responses = paths
            .GetProperty(path)
            .GetProperty(operation)
            .GetProperty("responses");

        foreach (string expectedStatus in expectedStatuses)
        {
            responses.TryGetProperty(expectedStatus, out _).Should().BeTrue(
                $"{operation.ToUpperInvariant()} {path} should document {expectedStatus}");
        }
    }

    private async Task<(HttpResponseMessage Response, JsonElement Todo)> CreateTodoAsync()
    {
        CreateTodoRequest request = new CreateTodoRequest
        {
            Name = "Submit report",
            Description = "Monthly report",
            DueDate = new DateOnly(2026, 8, 31),
            Priority = TodoPriority.High,
        };
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/todos",
            request);
        JsonElement todo = await ReadJsonAsync(response);

        return (response, todo);
    }

    private async Task<HttpResponseMessage> DeleteTodoAsync(string id, long version)
    {
        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/todos/{id}")
        {
            Content = JsonContent.Create(new DeleteTodoRequest { Version = version }),
        };

        return await client.SendAsync(request);
    }
}
