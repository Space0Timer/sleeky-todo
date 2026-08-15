using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Api.Authentication;
using Sleeky.Todo.Api.Contracts.Assistant;
using Sleeky.Todo.Api.Contracts.Todos;
using Sleeky.Todo.Assistant.Conflicts;
using Sleeky.Todo.Assistant.Providers;
using Sleeky.Todo.Assistant.Tools;
using Sleeky.Todo.Assistant.Turns;
using Sleeky.Todo.Domain.Enums;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Api;

[TestClass]
public sealed class AssistantApiTests
{
    private static readonly Guid UserId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly Guid OtherUserId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

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

        databaseName = $"sleekyTodoAssistantApiTests_{Guid.NewGuid():N}";
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

    /// <summary>
    /// The transport end to end: the response is a live event stream, and the
    /// turn opens and closes with the events a client's reducer depends on. No
    /// provider is configured here, which is exactly the path that needs no
    /// key to exercise.
    /// </summary>
    [TestMethod]
    public async Task TurnStreamsServerSentEventsAndClosesWithTheTranscript()
    {
        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/assistant/turns")
        {
            Content = JsonContent.Create(new AssistantTurnRequest
            {
                Message = "What is due today?",
            }),
        };

        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType
            .Should().Be("text/event-stream");

        IReadOnlyList<string> events = await ReadEventNamesAsync(response);

        events.Should().Equal(
            TurnEventType.TurnStarted,
            TurnEventType.Message,
            TurnEventType.TurnCompleted);
    }

    /// <summary>
    /// A key can be replaced but never retrieved, so no response on this route
    /// can carry one.
    /// </summary>
    [TestMethod]
    public async Task SettingsNeverHandBackTheStoredKey()
    {
        const string secret = "sk-integration-secret-value";

        using HttpResponseMessage saved = await client.PutAsJsonAsync(
            "/api/assistant/settings",
            new SaveAssistantSettingsRequest
            {
                Provider = nameof(AssistantProvider.Anthropic),
                Model = "claude-sonnet-5",
                ApiKey = secret,
            });

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await saved.Content.ReadAsStringAsync()).Should().NotContain(secret);

        using HttpResponseMessage read = await client.GetAsync("/api/assistant/settings");
        string body = await read.Content.ReadAsStringAsync();

        body.Should().NotContain(secret);
        AssistantSettingsView? view = JsonSerializer.Deserialize<AssistantSettingsView>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        view!.HasKey.Should().BeTrue();
        view.Model.Should().Be("claude-sonnet-5");
    }

    /// <summary>
    /// What lands in MongoDB has to be worth nothing on its own.
    /// </summary>
    [TestMethod]
    public async Task SettingsStoreTheKeyAsCiphertext()
    {
        const string secret = "sk-integration-secret-value";

        using HttpResponseMessage saved = await client.PutAsJsonAsync(
            "/api/assistant/settings",
            new SaveAssistantSettingsRequest
            {
                Provider = nameof(AssistantProvider.Anthropic),
                Model = "claude-sonnet-5",
                ApiKey = secret,
            });
        saved.EnsureSuccessStatusCode();

        MongoClient mongoClient = new MongoClient(mongoDbContainer!.GetConnectionString());
        BsonDocument stored = await mongoClient
            .GetDatabase(databaseName)
            .GetCollection<BsonDocument>("assistantSettings")
            .Find(Builders<BsonDocument>.Filter.Empty)
            .FirstAsync();

        stored.ToString().Should().NotContain(secret);
        stored.Contains("protectedApiKey").Should().BeTrue();
    }

    [TestMethod]
    public async Task DeletingSettingsThatWereNeverSavedReportsNothingToRemove()
    {
        using HttpResponseMessage response = await client.DeleteAsync("/api/assistant/settings");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The assistant dispatches the same commands the HTTP API does, so it
    /// inherits the ownership scoping enforced in the persistence boundary. A
    /// batch naming someone else's TODO fails rather than touching it.
    /// </summary>
    [TestMethod]
    public async Task AssistantWritesCannotReachAnotherOwnersTodo()
    {
        using HttpClient other = await factory.CreateAuthenticatedClientAsync(OtherUserId);
        JsonElement foreignTodo = await CreateTodoAsync(other, "Not yours");
        string foreignId = foreignTodo.GetProperty("id").GetString()!;

        TodoTools tools = BuildToolsFor(UserId, out RecordedEvents events);
        object read = await tools.GetTodoSelectionAsync(
            new[] { foreignId },
            CancellationToken.None);

        read.Should().BeOfType<TodoPage>()
            .Which.Items.Should().BeEmpty();

        object write = await tools.ChangeTodoStatusAsync(
            nameof(TodoStatus.Completed),
            new[] { foreignId },
            CancellationToken.None);

        write.Should().BeOfType<ToolFailure>();
        events.Published.Should().NotContain(TurnEventType.TodosChanged);

        JsonElement unchanged = await ReadTodoAsync(other, foreignId);
        unchanged.GetProperty("status").GetInt32()
            .Should().Be((int)TodoStatus.NotStarted);
        unchanged.GetProperty("version").GetInt64()
            .Should().Be(foreignTodo.GetProperty("version").GetInt64());
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonElement> CreateTodoAsync(HttpClient owner, string name)
    {
        using HttpResponseMessage response = await owner.PostAsJsonAsync(
            "/api/todos",
            new CreateTodoRequest
            {
                Name = name,
                DueDate = new DateOnly(2026, 9, 30),
                Priority = TodoPriority.Medium,
            });

        response.EnsureSuccessStatusCode();

        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadTodoAsync(HttpClient owner, string id)
    {
        using HttpResponseMessage response = await owner.GetAsync($"/api/todos/{id}");
        response.EnsureSuccessStatusCode();

        return await ReadJsonAsync(response);
    }

    /// <summary>
    /// Read as JSON rather than as the application DTO, which has no public
    /// constructor to deserialize through. The wire shape is what a client
    /// actually sees, so asserting on it is the more faithful check anyway.
    /// </summary>
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement;
    }

    private static async Task<IReadOnlyList<string>> ReadEventNamesAsync(
        HttpResponseMessage response)
    {
        List<string> names = new List<string>();
        using StreamReader reader = new StreamReader(
            await response.Content.ReadAsStreamAsync(),
            Encoding.UTF8);

        while (await reader.ReadLineAsync() is string line)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                names.Add(line["event:".Length..].Trim());
            }
        }

        return names;
    }

    /// <summary>
    /// Builds the tool layer against the running host's own services, as the
    /// given user, so the write goes through the real pipeline and the real
    /// database rather than a substitute for either.
    /// </summary>
    private TodoTools BuildToolsFor(Guid userId, out RecordedEvents events)
    {
        IServiceScope scope = factory.Services.CreateScope();
        IHttpContextAccessor accessor = scope.ServiceProvider
            .GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(TodoClaimTypes.UserId, userId.ToString()) },
                "Testing")),
        };

        events = new RecordedEvents();

        return new TodoTools(
            scope.ServiceProvider.GetRequiredService<ISender>(),
            scope.ServiceProvider.GetRequiredService<IBulkConflictPolicy>(),
            new TodoVersionLedger(),
            events,
            new NoOpTurnController(),
            NullLogger<TodoTools>.Instance);
    }

    private sealed class RecordedEvents : ITurnEventWriter
    {
        private readonly List<string> published = new List<string>();

        public IReadOnlyList<string> Published => this.published;

        public ValueTask PublishAsync(TurnEvent turnEvent, CancellationToken cancellationToken)
        {
            this.published.Add(turnEvent.Type);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpTurnController : ITurnController
    {
        public void Halt()
        {
        }
    }
}
