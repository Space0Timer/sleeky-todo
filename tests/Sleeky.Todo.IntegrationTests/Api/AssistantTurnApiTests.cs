using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using MongoDB.Driver;

using Sleeky.Todo.Api.Contracts.Assistant;
using Sleeky.Todo.Api.Contracts.Todos;
using Sleeky.Todo.Assistant.Providers;
using Sleeky.Todo.Assistant.Tools;
using Sleeky.Todo.Assistant.Turns;
using Sleeky.Todo.Domain.Enums;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// One turn, end to end, with only the provider replaced.
/// </summary>
/// <remarks>
/// The unit suite proves the loop does the right thing and the API suite proves
/// the stream is live, but between them sat a seam nothing crossed: the tool
/// traffic a real turn produces had never travelled over HTTP. This runs a
/// read-then-write turn through the event stream, the loop, the tool layer, the
/// MediatR pipeline, and MongoDB, and checks the TODO actually moved.
/// </remarks>
[TestClass]
public sealed class AssistantTurnApiTests
{
    private static readonly Guid UserId =
        Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

    private static MongoDbContainer? mongoDbContainer;

    private HttpClient client = null!;
    private Guid spaceId;
    private string databaseName = null!;
    private TodoApiFactory factory = null!;
    private ScriptedChatClientFactory clients = null!;

    /// <summary>
    /// Every TODO route hangs off the Space the suite seeded.
    /// </summary>
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

        databaseName = $"sleekyTodoAssistantTurnTests_{Guid.NewGuid():N}";
        clients = new ScriptedChatClientFactory();
        factory = new TodoApiFactory(
            mongoDbContainer.GetConnectionString(),
            databaseName,
            configureServices: services =>
            {
                services.AddSingleton<IChatClientFactory>(clients);

                // An application-level key, so a connection resolves without a
                // user having stored one. The value is never used: the factory
                // above answers before anything reaches a provider.
                services.AddSingleton<IOptions<AssistantOptions>>(Options.Create(
                    new AssistantOptions
                    {
                        Provider = AssistantProvider.Anthropic,
                        Model = "scripted",
                        ApiKey = "not-a-real-key",
                    }));
            });
        client = await factory.CreateAuthenticatedClientAsync(UserId);
        spaceId = await factory.CreateSpaceAsync(UserId);
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
    public async Task ATurnReadsWritesAndReportsWhatItDidOverTheStream()
    {
        JsonElement todo = await CreateTodoAsync("Submit the report");
        string id = todo.GetProperty("id").GetString()!;

        clients.Client.Script(
            ScriptedChatClient.Calls(
                TodoToolNames.GetTodos,
                new Dictionary<string, object?> { ["limit"] = 50 }),
            ScriptedChatClient.Calls(
                TodoToolNames.ChangeTodoStatus,
                new Dictionary<string, object?>
                {
                    ["status"] = "Completed",
                    ["ids"] = new[] { id },
                }),
            ScriptedChatClient.Says("Marked 1 completed."));

        IReadOnlyList<(string Event, string Data)> events = await RunTurnAsync(
            new AssistantTurnRequest { SpaceId = spaceId, Message = "Complete the report." });

        events.Select(entry => entry.Event).Should().ContainInOrder(
            TurnEventType.TurnStarted,
            TurnEventType.ToolExecuted,
            TurnEventType.TodosChanged,
            TurnEventType.Message,
            TurnEventType.TurnCompleted);

        Payload(events, TurnEventType.Message)
            .GetProperty("text").GetString().Should().Be("Marked 1 completed.");
        Payload(events, TurnEventType.TodosChanged)
            .GetProperty("ids").EnumerateArray().Select(value => value.GetString())
            .Should().Contain(id);

        // The write reached the database through the same pipeline the HTTP API
        // uses, rather than stopping at a substitute for it.
        JsonElement updated = await ReadTodoAsync(id);
        updated.GetProperty("status").GetInt32().Should().Be((int)TodoStatus.Completed);
        updated.GetProperty("version").GetInt64().Should().Be(2);
    }

    /// <summary>
    /// The conversation has to survive the round trip, tool traffic included:
    /// the server keeps no history, so a transcript that lost the tool results
    /// would leave the next turn unable to write to anything read in this one.
    /// </summary>
    [TestMethod]
    public async Task ATurnHandsBackATranscriptTheNextTurnCanWriteFrom()
    {
        JsonElement todo = await CreateTodoAsync("Carry me forward");
        string id = todo.GetProperty("id").GetString()!;

        clients.Client.Script(
            ScriptedChatClient.Calls(
                TodoToolNames.GetTodos,
                new Dictionary<string, object?> { ["limit"] = 50 }),
            ScriptedChatClient.Says("You have one TODO."));

        IReadOnlyList<(string Event, string Data)> first = await RunTurnAsync(
            new AssistantTurnRequest { SpaceId = spaceId, Message = "What is on my list?" });
        JsonElement transcript = Payload(first, TurnEventType.TurnCompleted)
            .GetProperty("messages");

        // A second turn that only writes. It can succeed solely because the
        // echoed transcript still carries what the first turn read.
        clients.Client.Script(
            ScriptedChatClient.Calls(
                TodoToolNames.ChangeTodoStatus,
                new Dictionary<string, object?>
                {
                    ["status"] = "Completed",
                    ["ids"] = new[] { id },
                }),
            ScriptedChatClient.Says("Done."));

        IReadOnlyList<(string Event, string Data)> second = await RunTurnAsync(
            new AssistantTurnRequest
            {
                SpaceId = spaceId,
                Message = "Complete it.",
                Transcript = transcript,
            });

        second.Select(entry => entry.Event).Should().Contain(TurnEventType.TodosChanged);
        (await ReadTodoAsync(id)).GetProperty("status").GetInt32()
            .Should().Be((int)TodoStatus.Completed);
    }

    /// <summary>
    /// A destructive proposal stops the turn and reaches the browser as a
    /// question, carrying the state it read when it made the proposal.
    /// </summary>
    [TestMethod]
    public async Task ADeletionProposalAsksOverTheStreamAndDeletesNothing()
    {
        JsonElement todo = await CreateTodoAsync("Ask before deleting");
        string id = todo.GetProperty("id").GetString()!;

        clients.Client.Script(
            ScriptedChatClient.Calls(
                TodoToolNames.DeleteTodos,
                new Dictionary<string, object?> { ["ids"] = new[] { id } }));

        IReadOnlyList<(string Event, string Data)> events = await RunTurnAsync(
            new AssistantTurnRequest { SpaceId = spaceId, Message = "Delete it." });

        events.Select(entry => entry.Event)
            .Should().Contain(TurnEventType.ConfirmationRequired)
            .And.NotContain(TurnEventType.TodosChanged);

        JsonElement confirmation = Payload(events, TurnEventType.ConfirmationRequired);
        confirmation.GetProperty("tool").GetString().Should().Be(TodoToolNames.DeleteTodos);
        JsonElement item = confirmation.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("id").GetString().Should().Be(id);
        item.GetProperty("version").GetInt64().Should().Be(1);

        (await ProbeTodoAsync(id)).GetProperty("deletedAt").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// The confirming turn runs what the person agreed to. The model is not
    /// asked again, so the script holds only the summary.
    /// </summary>
    [TestMethod]
    public async Task AConfirmedDeletionAppliesWithTheVersionsItDisplayed()
    {
        JsonElement todo = await CreateTodoAsync("Confirmed for deletion");
        string id = todo.GetProperty("id").GetString()!;

        clients.Client.Script(ScriptedChatClient.Says("Deleted it."));

        IReadOnlyList<(string Event, string Data)> events = await RunTurnAsync(
            new AssistantTurnRequest
            {
                SpaceId = spaceId,
                Confirmation = new AssistantConfirmationRequest
                {
                    Tool = TodoToolNames.DeleteTodos,
                    Items = new[]
                    {
                        new AssistantConfirmationItem { Id = Guid.Parse(id), Version = 1 },
                    },
                },
            });

        events.Select(entry => entry.Event).Should().Contain(TurnEventType.TodosChanged);

        // Read through the selection probe rather than the single-item route,
        // which does not report a soft-deleted TODO at all.
        (await ProbeTodoAsync(id)).GetProperty("deletedAt").ValueKind
            .Should().NotBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The Space check is the turn's first act, so a turn naming a Space the
    /// caller cannot see is an ordinary 404 rather than a stream that opens and
    /// then stops — and the model is never asked anything.
    /// </summary>
    /// <remarks>
    /// The status code is the point of doing it in the controller as well as in
    /// the runner: once a server-sent event response has started, an exception
    /// can no longer become one.
    /// </remarks>
    [TestMethod]
    public async Task ATurnInASpaceTheUserCannotSeeIsRefusedBeforeTheModel()
    {
        clients.Client.Script(ScriptedChatClient.Says("Never sent."));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/assistant/turns",
            new AssistantTurnRequest
            {
                SpaceId = Guid.NewGuid(),
                Message = "What is on my list?",
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        clients.Client.CallCount.Should().Be(0);
    }

    /// <summary>
    /// A turn has to name a Space, and one that does not is a field error on
    /// the request rather than a turn that runs against nothing.
    /// </summary>
    [TestMethod]
    public async Task ATurnWithoutASpaceIsAValidationFailure()
    {
        clients.Client.Script(ScriptedChatClient.Says("Never sent."));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/assistant/turns",
            new AssistantTurnRequest { Message = "What is on my list?" });
        JsonElement problem = JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement;

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").TryGetProperty("spaceId", out JsonElement errors)
            .Should().BeTrue();
        errors.EnumerateArray().Select(value => value.GetString())
            .Should().Contain("A Space identifier is required.");
        clients.Client.CallCount.Should().Be(0);
    }

    /// <summary>
    /// A confirmation carries no Space of its own, so it executes in the Space
    /// the turn names. One replayed against a Space the caller cannot see is
    /// refused before it is applied, which is why the check precedes the
    /// confirmation rather than following it.
    /// </summary>
    [TestMethod]
    public async Task AConfirmationInAnUnreachableSpaceDeletesNothing()
    {
        JsonElement todo = await CreateTodoAsync("Still here");
        string id = todo.GetProperty("id").GetString()!;
        clients.Client.Script(ScriptedChatClient.Says("Never sent."));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/assistant/turns",
            new AssistantTurnRequest
            {
                SpaceId = Guid.NewGuid(),
                Confirmation = new AssistantConfirmationRequest
                {
                    Tool = TodoToolNames.DeleteTodos,
                    Items = new[]
                    {
                        new AssistantConfirmationItem { Id = Guid.Parse(id), Version = 1 },
                    },
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        clients.Client.CallCount.Should().Be(0);
        (await ReadTodoAsync(id)).GetProperty("deletedAt").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement Payload(
        IReadOnlyList<(string Event, string Data)> events,
        string name)
    {
        (string Event, string Data) found = events.Single(entry => entry.Event == name);

        return JsonDocument.Parse(found.Data).RootElement.GetProperty("data");
    }

    private async Task<IReadOnlyList<(string Event, string Data)>> RunTurnAsync(
        AssistantTurnRequest request)
    {
        using HttpRequestMessage message = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/assistant/turns")
        {
            Content = JsonContent.Create(request),
        };
        using HttpResponseMessage response = await client.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        List<(string Event, string Data)> events = new List<(string, string)>();
        string? name = null;
        using StreamReader reader = new StreamReader(
            await response.Content.ReadAsStreamAsync(),
            Encoding.UTF8);

        while (await reader.ReadLineAsync() is string line)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal) && name is not null)
            {
                events.Add((name, line["data:".Length..].Trim()));
                name = null;
            }
        }

        return events;
    }

    private async Task<JsonElement> CreateTodoAsync(string name)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Todos}",
            new CreateTodoRequest
            {
                Name = name,
                DueDate = new DateOnly(2026, 9, 30),
                Priority = TodoPriority.Medium,
            });

        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    /// <summary>
    /// The found-only probe, which reports soft-deleted TODOs where the
    /// single-item route answers 404.
    /// </summary>
    private async Task<JsonElement> ProbeTodoAsync(string id)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{Todos}/selection?id={id}");
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("items").EnumerateArray().Single();
    }

    private async Task<JsonElement> ReadTodoAsync(string id)
    {
        using HttpResponseMessage response = await client.GetAsync($"{Todos}/{id}");
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
