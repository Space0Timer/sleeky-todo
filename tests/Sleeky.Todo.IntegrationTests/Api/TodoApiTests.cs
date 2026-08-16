using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Api.Contracts.Todos;
using Sleeky.Todo.Application.Todos.Validation;
using Sleeky.Todo.Domain.Enums;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Api;

[TestClass]
public sealed class TodoApiTests
{
    private static readonly Guid UserId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid OtherUserId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

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

        databaseName = $"sleekyTodoApiTests_{Guid.NewGuid():N}";
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

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement deletedTodo = await ReadJsonAsync(deleteResponse);
        deletedTodo.GetProperty("version").GetInt64().Should().Be(2);
        deletedTodo.GetProperty("deletedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
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
    public async Task DependencyCanBeAddedAndRemoved()
    {
        (_, JsonElement source) = await CreateTodoAsync();
        (_, JsonElement dependency) = await CreateTodoAsync();
        string sourceId = GetTodoId(source);
        string dependencyId = GetTodoId(dependency);

        HttpResponseMessage addResponse = await AddDependencyAsync(
            sourceId,
            dependencyId,
            version: 1);
        JsonElement withDependency = await ReadJsonAsync(addResponse);
        HttpResponseMessage removeResponse = await RemoveDependencyAsync(
            sourceId,
            dependencyId,
            version: 2);
        JsonElement withoutDependency = await ReadJsonAsync(removeResponse);

        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        withDependency.GetProperty("dependencyIds")[0].GetString()
            .Should().Be(dependencyId);
        withDependency.GetProperty("version").GetInt64().Should().Be(2);
        removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        withoutDependency.GetProperty("dependencyIds").GetArrayLength().Should().Be(0);
        withoutDependency.GetProperty("version").GetInt64().Should().Be(3);
    }

    [TestMethod]
    public async Task SelfDuplicateMissingAndDeletedDependenciesAreRejected()
    {
        (_, JsonElement source) = await CreateTodoAsync();
        (_, JsonElement dependency) = await CreateTodoAsync();
        string sourceId = GetTodoId(source);
        string dependencyId = GetTodoId(dependency);

        HttpResponseMessage selfResponse = await AddDependencyAsync(
            sourceId,
            sourceId,
            version: 1);
        _ = await AddDependencyAsync(sourceId, dependencyId, version: 1);
        HttpResponseMessage duplicateResponse = await AddDependencyAsync(
            sourceId,
            dependencyId,
            version: 2);
        HttpResponseMessage missingResponse = await AddDependencyAsync(
            sourceId,
            Guid.NewGuid().ToString("D"),
            version: 2);
        (_, JsonElement deletedDependency) = await CreateTodoAsync();
        string deletedDependencyId = GetTodoId(deletedDependency);
        _ = await DeleteTodoAsync(deletedDependencyId, version: 1);
        HttpResponseMessage deletedResponse = await AddDependencyAsync(
            sourceId,
            deletedDependencyId,
            version: 2);

        selfResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task DirectAndMultiLevelDependencyCyclesAreRejected()
    {
        (_, JsonElement first) = await CreateTodoAsync();
        (_, JsonElement second) = await CreateTodoAsync();
        (_, JsonElement third) = await CreateTodoAsync();
        string firstId = GetTodoId(first);
        string secondId = GetTodoId(second);
        string thirdId = GetTodoId(third);

        _ = await AddDependencyAsync(firstId, secondId, version: 1);
        HttpResponseMessage directCycle = await AddDependencyAsync(
            secondId,
            firstId,
            version: 1);
        _ = await AddDependencyAsync(secondId, thirdId, version: 1);
        HttpResponseMessage multiLevelCycle = await AddDependencyAsync(
            thirdId,
            firstId,
            version: 1);
        JsonElement directProblem = await ReadJsonAsync(directCycle);
        JsonElement multiLevelProblem = await ReadJsonAsync(multiLevelCycle);

        directCycle.StatusCode.Should().Be(HttpStatusCode.Conflict);
        multiLevelCycle.StatusCode.Should().Be(HttpStatusCode.Conflict);
        directProblem.GetProperty("detail").GetString()
            .Should().Be("Adding this dependency would create a cycle.");
        multiLevelProblem.GetProperty("detail").GetString()
            .Should().Be("Adding this dependency would create a cycle.");
    }

    [TestMethod]
    public async Task BlockedStatusTransitionsSucceedAfterPrerequisiteCompletes()
    {
        (_, JsonElement prerequisite) = await CreateTodoAsync();
        (_, JsonElement dependent) = await CreateTodoAsync();
        string prerequisiteId = GetTodoId(prerequisite);
        string dependentId = GetTodoId(dependent);
        _ = await AddDependencyAsync(dependentId, prerequisiteId, version: 1);

        HttpResponseMessage blockedListResponse = await client.GetAsync(
            "/api/todos?dependencyStatus=Blocked");
        JsonElement blockedList = await ReadJsonAsync(blockedListResponse);

        HttpResponseMessage blockedInProgress = await ChangeStatusAsync(
            dependentId,
            TodoStatus.InProgress,
            version: 2);
        HttpResponseMessage blockedCompleted = await ChangeStatusAsync(
            dependentId,
            TodoStatus.Completed,
            version: 2);
        HttpResponseMessage completePrerequisite = await ChangeStatusAsync(
            prerequisiteId,
            TodoStatus.Completed,
            version: 1);
        HttpResponseMessage unblocked = await ChangeStatusAsync(
            dependentId,
            TodoStatus.InProgress,
            version: 2);
        JsonElement unblockedTodo = await ReadJsonAsync(unblocked);
        HttpResponseMessage unblockedListResponse = await client.GetAsync(
            "/api/todos?dependencyStatus=Unblocked");
        JsonElement unblockedList = await ReadJsonAsync(unblockedListResponse);

        blockedListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        blockedList.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Should().Contain(dependentId);
        blockedInProgress.StatusCode.Should().Be(HttpStatusCode.Conflict);
        blockedCompleted.StatusCode.Should().Be(HttpStatusCode.Conflict);
        completePrerequisite.StatusCode.Should().Be(HttpStatusCode.OK);
        unblocked.StatusCode.Should().Be(HttpStatusCode.OK);
        unblockedTodo.GetProperty("status").GetInt32()
            .Should().Be((int)TodoStatus.InProgress);
        unblockedTodo.GetProperty("version").GetInt64().Should().Be(3);
        unblockedListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        unblockedList.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Should().Contain(dependentId);
    }

    [TestMethod]
    public async Task ActivePrerequisiteCannotBeDeletedAndMutationsRejectStaleVersions()
    {
        (_, JsonElement prerequisite) = await CreateTodoAsync();
        (_, JsonElement dependent) = await CreateTodoAsync();
        string prerequisiteId = GetTodoId(prerequisite);
        string dependentId = GetTodoId(dependent);
        _ = await AddDependencyAsync(dependentId, prerequisiteId, version: 1);

        HttpResponseMessage deleteResponse = await DeleteTodoAsync(
            prerequisiteId,
            version: 1);
        HttpResponseMessage staleRemove = await RemoveDependencyAsync(
            dependentId,
            prerequisiteId,
            version: 1);
        _ = await ChangeStatusAsync(
            prerequisiteId,
            TodoStatus.Archived,
            version: 1);
        HttpResponseMessage staleStatus = await ChangeStatusAsync(
            prerequisiteId,
            TodoStatus.Open,
            version: 1);

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        staleRemove.StatusCode.Should().Be(HttpStatusCode.Conflict);
        staleStatus.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [TestMethod]
    public async Task CompletingRecurringTodoCreatesExactlyOneNextOccurrence()
    {
        (_, JsonElement prerequisite) = await CreateTodoAsync();
        string prerequisiteId = GetTodoId(prerequisite);
        HttpResponseMessage completedPrerequisite = await ChangeStatusAsync(
            prerequisiteId,
            TodoStatus.Completed,
            version: 1);
        completedPrerequisite.StatusCode.Should().Be(HttpStatusCode.OK);

        (_, JsonElement recurring) = await CreateTodoAsync(
            new RecurrenceRequest
            {
                Type = RecurrenceType.Monthly,
                Interval = 1,
            });
        string recurringId = GetTodoId(recurring);
        string seriesId = recurring.GetProperty("seriesId").GetString()
            ?? throw new InvalidOperationException("A recurring TODO requires a series ID.");
        HttpResponseMessage addedDependency = await AddDependencyAsync(
            recurringId,
            prerequisiteId,
            version: 1);
        addedDependency.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage completion = await ChangeStatusAsync(
            recurringId,
            TodoStatus.Completed,
            version: 2);
        JsonElement completed = await ReadJsonAsync(completion);
        string nextOccurrenceId = completed
            .GetProperty("nextOccurrenceId")
            .GetString()
            ?? throw new InvalidOperationException(
                "A recurring completion requires a next occurrence ID.");
        HttpResponseMessage nextResponse = await client.GetAsync(
            $"/api/todos/{nextOccurrenceId}");
        JsonElement next = await ReadJsonAsync(nextResponse);
        HttpResponseMessage repeatedCompletion = await ChangeStatusAsync(
            recurringId,
            TodoStatus.Completed,
            version: 3);
        JsonElement repeated = await ReadJsonAsync(repeatedCompletion);

        completion.StatusCode.Should().Be(HttpStatusCode.OK);
        completed.GetProperty("version").GetInt64().Should().Be(3);
        nextResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        next.GetProperty("dueDate").GetString().Should().Be("2026-09-30");
        next.GetProperty("status").GetInt32().Should().Be((int)TodoStatus.Open);
        next.GetProperty("seriesId").GetString().Should().Be(seriesId);
        next.GetProperty("occurrenceNumber").GetInt32().Should().Be(2);
        next.GetProperty("dependencyIds").GetArrayLength().Should().Be(0);
        repeatedCompletion.StatusCode.Should().Be(HttpStatusCode.OK);
        repeated.GetProperty("nextOccurrenceId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [TestMethod]
    public async Task FailedNextOccurrenceInsertionRollsBackCompletion()
    {
        (_, JsonElement recurring) = await CreateTodoAsync(
            new RecurrenceRequest
            {
                Type = RecurrenceType.Daily,
                Interval = 1,
            });
        string recurringId = GetTodoId(recurring);
        string seriesId = recurring.GetProperty("seriesId").GetString()
            ?? throw new InvalidOperationException("A recurring TODO requires a series ID.");
        IMongoCollection<BsonDocument> collection = GetTodoCollection();
        BsonDocument duplicateOccurrence = await collection
            .Find(new BsonDocument("_id", StandardUuid(recurringId)))
            .FirstAsync();
        duplicateOccurrence["_id"] = StandardUuid(Guid.NewGuid());
        duplicateOccurrence["occurrenceNumber"] = 2;
        await collection.InsertOneAsync(duplicateOccurrence);

        HttpResponseMessage completion = await ChangeStatusAsync(
            recurringId,
            TodoStatus.Completed,
            version: 1);
        HttpResponseMessage currentResponse = await client.GetAsync(
            $"/api/todos/{recurringId}");
        JsonElement current = await ReadJsonAsync(currentResponse);
        long seriesCount = await collection.CountDocumentsAsync(
            new BsonDocument("seriesId", StandardUuid(seriesId)));

        completion.StatusCode.Should().Be(HttpStatusCode.Conflict);
        currentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        current.GetProperty("status").GetInt32()
            .Should().Be((int)TodoStatus.Open);
        current.GetProperty("version").GetInt64().Should().Be(1);
        seriesCount.Should().Be(2);
    }

    [TestMethod]
    public async Task ConcurrentRecurringCompletionCreatesOneNextOccurrence()
    {
        (_, JsonElement recurring) = await CreateTodoAsync(
            new RecurrenceRequest
            {
                Type = RecurrenceType.Weekly,
                Interval = 1,
            });
        string recurringId = GetTodoId(recurring);
        string seriesId = recurring.GetProperty("seriesId").GetString()
            ?? throw new InvalidOperationException("A recurring TODO requires a series ID.");

        Task<HttpResponseMessage> first = ChangeStatusAsync(
            recurringId,
            TodoStatus.Completed,
            version: 1);
        Task<HttpResponseMessage> second = ChangeStatusAsync(
            recurringId,
            TodoStatus.Completed,
            version: 1);
        HttpResponseMessage[] responses = await Task.WhenAll(first, second);
        long seriesCount = await GetTodoCollection().CountDocumentsAsync(
            new BsonDocument("seriesId", StandardUuid(seriesId)));

        responses.Select(response => response.StatusCode).Should().BeEquivalentTo(
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        seriesCount.Should().Be(2);
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
    public async Task InvalidRecurrenceReturnsValidationProblem()
    {
        CreateTodoRequest request = new CreateTodoRequest
        {
            Name = "Submit report",
            DueDate = new DateOnly(2026, 8, 31),
            Priority = TodoPriority.High,
            Recurrence = new RecurrenceRequest
            {
                Type = RecurrenceType.Custom,
                Interval = 2,
            },
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/todos",
            request);
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("errors")
            .TryGetProperty("recurrenceUnit", out _)
            .Should().BeTrue();
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
    public async Task ListRejectsMalformedCursorAndPageSizeAboveMaximum()
    {
        HttpResponseMessage cursorResponse = await client.GetAsync(
            "/api/todos?cursor=not%2Ba%2Bbase64url%2Bcursor");
        JsonElement cursorProblem = await ReadJsonAsync(cursorResponse);
        HttpResponseMessage limitResponse = await client.GetAsync(
            "/api/todos?limit=101");
        JsonElement limitProblem = await ReadJsonAsync(limitResponse);

        cursorResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        cursorProblem.GetProperty("title").GetString().Should().Be("Invalid cursor.");
        limitResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        limitProblem.GetProperty("title").GetString().Should().Be("Validation failed.");
    }

    [TestMethod]
    public async Task ListRejectsCursorReusedWithDifferentFilters()
    {
        _ = await CreateTodoAsync();
        _ = await CreateTodoAsync();
        HttpResponseMessage firstResponse = await client.GetAsync(
            "/api/todos?priority=High&limit=1");
        JsonElement firstPage = await ReadJsonAsync(firstResponse);
        string cursor = firstPage.GetProperty("nextCursor").GetString()
            ?? throw new InvalidOperationException("The first page should provide a cursor.");

        HttpResponseMessage response = await client.GetAsync(
            $"/api/todos?priority=Low&limit=1&cursor={Uri.EscapeDataString(cursor)}");
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("title").GetString().Should().Be("Invalid cursor.");
        problem.GetProperty("detail").GetString().Should().Contain("does not match");
    }

    [TestMethod]
    public async Task ListNarrowsToTheSearchedTodos()
    {
        _ = await CreateTodoAsync(name: "Submit quarterly report");
        _ = await CreateTodoAsync(name: "Book a haircut");

        HttpResponseMessage response = await client.GetAsync("/api/todos?search=quart");
        JsonElement page = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should().Equal("Submit quarterly report");
    }

    [TestMethod]
    public async Task ListRejectsSearchTextBeyondTheMaximumLength()
    {
        HttpResponseMessage response = await client.GetAsync(
            $"/api/todos?search={new string('a', TodoValidationLimits.SearchTextMaximumLength + 1)}");
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("title").GetString().Should().Be("Validation failed.");
        problem.GetProperty("errors").TryGetProperty("searchText", out _)
            .Should().BeTrue();
    }

    /// <summary>
    /// A cursor is bound to the search that produced it, so continuing a page
    /// under different search text is refused rather than answered with a page
    /// from a different result set.
    /// </summary>
    [TestMethod]
    public async Task ListRejectsCursorReusedWithDifferentSearchText()
    {
        _ = await CreateTodoAsync(name: "Submit quarterly report");
        _ = await CreateTodoAsync(name: "Submit annual report");
        HttpResponseMessage firstResponse = await client.GetAsync(
            "/api/todos?search=submit&limit=1");
        JsonElement firstPage = await ReadJsonAsync(firstResponse);
        string cursor = firstPage.GetProperty("nextCursor").GetString()
            ?? throw new InvalidOperationException("The first page should provide a cursor.");

        HttpResponseMessage response = await client.GetAsync(
            $"/api/todos?search=report&limit=1&cursor={Uri.EscapeDataString(cursor)}");
        JsonElement problem = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("title").GetString().Should().Be("Invalid cursor.");
        problem.GetProperty("detail").GetString().Should().Contain("does not match");
    }

    [TestMethod]
    public async Task SwaggerIsNotPublishedOutsideDevelopment()
    {
        HttpResponseMessage response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task SwaggerGenerationDocumentsExpectedResponses()
    {
        using TodoApiFactory developmentFactory = new TodoApiFactory(
            mongoDbContainer!.GetConnectionString(),
            databaseName,
            TodoApiFactory.DevelopmentEnvironment);
        using HttpClient developmentClient = developmentFactory.CreateClient();

        HttpResponseMessage response = await developmentClient.GetAsync(
            "/swagger/v1/swagger.json");
        JsonElement swagger = await ReadJsonAsync(response);
        JsonElement paths = swagger.GetProperty("paths");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertResponses(paths, "/api/todos", "get", "200", "400");
        AssertResponses(paths, "/api/todos", "post", "201", "400", "409");
        AssertResponses(paths, "/api/todos/{id}", "get", "200", "400", "404");
        AssertResponses(paths, "/api/todos/{id}", "put", "200", "400", "404", "409");
        AssertResponses(paths, "/api/todos/{id}", "delete", "200", "400", "404", "409");
        AssertResponses(
            paths,
            "/api/todos/{id}/dependencies",
            "post",
            "200",
            "400",
            "404",
            "409");
        AssertResponses(
            paths,
            "/api/todos/{id}/dependencies/{dependencyId}",
            "delete",
            "200",
            "400",
            "404",
            "409");
        AssertResponses(
            paths,
            "/api/todos/{id}/status",
            "put",
            "200",
            "400",
            "404",
            "409");
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

        indexNames.Should().Contain("owner_active_due_date_id");
        indexNames.Should().Contain("owner_active_priority_id");
        indexNames.Should().Contain("owner_active_status_id");
        indexNames.Should().Contain("owner_active_name_normalized_id");
        indexNames.Should().Contain("owner_active_dependency_ids");
        indexNames.Should().Contain("owner_active_search_tokens");
        indexNames.Should().Contain("purge_at");
        indexNames.Should().Contain("owner_unique_series_occurrence");
        indexNames.Should().NotContain("active_due_date_id");
        indexNames.Should().NotContain("unique_series_occurrence");
        BsonDocument recurrenceIndex = indexes.Single(
            index => index["name"] == "owner_unique_series_occurrence");
        recurrenceIndex["unique"].AsBoolean.Should().BeTrue();
        recurrenceIndex["key"].AsBsonDocument.Names.First().Should().Be("ownerId");
    }

    [TestMethod]
    public async Task UnauthenticatedTodoRequestReturnsUnauthorized()
    {
        using HttpClient anonymous = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });

        HttpResponseMessage response = await anonymous.GetAsync("/api/todos");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task MutationWithoutAntiforgeryTokenIsRejected()
    {
        using HttpClient withoutToken = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
        withoutToken.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.UserIdHeaderName,
            UserId.ToString());

        HttpResponseMessage response = await withoutToken.PostAsJsonAsync(
            "/api/todos",
            new
            {
                name = "Submit report",
                dueDate = "2026-08-31",
                priority = "High",
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task AnotherUserCannotReadOrListThisUsersTodo()
    {
        (_, JsonElement created) = await CreateTodoAsync();
        Guid todoId = created.GetProperty("id").GetGuid();
        using HttpClient otherUser = await factory.CreateAuthenticatedClientAsync(
            OtherUserId);

        HttpResponseMessage detail = await otherUser.GetAsync($"/api/todos/{todoId}");
        HttpResponseMessage list = await otherUser.GetAsync("/api/todos");

        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument page = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        page.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    public async Task AnotherUserCannotDeleteThisUsersTodo()
    {
        (_, JsonElement created) = await CreateTodoAsync();
        Guid todoId = created.GetProperty("id").GetGuid();
        using HttpClient otherUser = await factory.CreateAuthenticatedClientAsync(
            OtherUserId);

        HttpResponseMessage response = await otherUser.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/todos/{todoId}")
            {
                Content = JsonContent.Create(new { version = 1 }),
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task CurrentUserEndpointReportsAuthenticationState()
    {
        using HttpClient anonymous = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });

        HttpResponseMessage anonymousResponse = await anonymous.GetAsync("/api/auth/me");
        HttpResponseMessage authenticatedResponse = await client.GetAsync("/api/auth/me");

        JsonDocument anonymousBody = JsonDocument.Parse(
            await anonymousResponse.Content.ReadAsStringAsync());
        JsonDocument authenticatedBody = JsonDocument.Parse(
            await authenticatedResponse.Content.ReadAsStringAsync());

        anonymousBody.RootElement.GetProperty("isAuthenticated")
            .GetBoolean().Should().BeFalse();
        authenticatedBody.RootElement.GetProperty("isAuthenticated")
            .GetBoolean().Should().BeTrue();
        authenticatedBody.RootElement.GetProperty("userId")
            .GetGuid().Should().Be(UserId);
    }

    [TestMethod]
    public async Task LoginRejectsExternalReturnUrl()
    {
        HttpResponseMessage response = await client.GetAsync(
            "/api/auth/login?returnUrl=https://attacker.example.com/steal");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task StartupMigratesStringTodoEnumsToIntegers()
    {
        client.Dispose();
        factory.Dispose();

        MongoClient mongoClient = new MongoClient(
            mongoDbContainer!.GetConnectionString());
        IMongoCollection<BsonDocument> collection = mongoClient
            .GetDatabase(databaseName)
            .GetCollection<BsonDocument>("todoItems");
        Guid documentId = Guid.NewGuid();
        BsonBinaryData persistedId = new BsonBinaryData(
            documentId,
            GuidRepresentation.Standard);
        await collection.InsertOneAsync(
            new BsonDocument
            {
                { "_id", persistedId },
                { "status", nameof(TodoStatus.Completed) },
                { "priority", nameof(TodoPriority.High) },
            });

        factory = new TodoApiFactory(
            mongoDbContainer.GetConnectionString(),
            databaseName);
        client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
        _ = await client.GetAsync("/health");

        BsonDocument migrated = await collection
            .Find(Builders<BsonDocument>.Filter.Eq("_id", persistedId))
            .SingleAsync();

        migrated["status"].AsInt32.Should().Be((int)TodoStatus.Completed);
        migrated["priority"].AsInt32.Should().Be((int)TodoPriority.High);
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

    private static BsonBinaryData StandardUuid(string value)
    {
        return StandardUuid(Guid.Parse(value));
    }

    private static BsonBinaryData StandardUuid(Guid value)
    {
        return new BsonBinaryData(value, GuidRepresentation.Standard);
    }

    private IMongoCollection<BsonDocument> GetTodoCollection()
    {
        MongoClient mongoClient = new MongoClient(
            mongoDbContainer!.GetConnectionString());
        return mongoClient
            .GetDatabase(databaseName)
            .GetCollection<BsonDocument>("todoItems");
    }

    private async Task<(HttpResponseMessage Response, JsonElement Todo)> CreateTodoAsync(
        RecurrenceRequest? recurrence = null,
        string name = "Submit report")
    {
        CreateTodoRequest request = new CreateTodoRequest
        {
            Name = name,
            Description = "Monthly report",
            DueDate = new DateOnly(2026, 8, 31),
            Priority = TodoPriority.High,
            Recurrence = recurrence,
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

    private async Task<HttpResponseMessage> AddDependencyAsync(
        string id,
        string dependencyId,
        long version)
    {
        return await client.PostAsJsonAsync(
            $"/api/todos/{id}/dependencies",
            new AddDependencyRequest
            {
                DependencyId = Guid.Parse(dependencyId),
                Version = version,
            });
    }

    private async Task<HttpResponseMessage> RemoveDependencyAsync(
        string id,
        string dependencyId,
        long version)
    {
        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/todos/{id}/dependencies/{dependencyId}")
        {
            Content = JsonContent.Create(new RemoveDependencyRequest { Version = version }),
        };

        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ChangeStatusAsync(
        string id,
        TodoStatus status,
        long version)
    {
        return await client.PutAsJsonAsync(
            $"/api/todos/{id}/status",
            new ChangeTodoStatusRequest
            {
                Status = status,
                Version = version,
            });
    }
}
