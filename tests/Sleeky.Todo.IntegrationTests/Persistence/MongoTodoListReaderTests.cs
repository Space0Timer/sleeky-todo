using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.DependencyInjection;
using Sleeky.Todo.Infrastructure.Persistence;

using Testcontainers.MongoDb;

using TodoSortDirection = Sleeky.Todo.Application.Todos.Queries.GetTodos.SortDirection;

namespace Sleeky.Todo.IntegrationTests.Persistence;

[TestClass]
public sealed class MongoTodoListReaderTests
{
    private static readonly Guid OwnerId = Id("owner-1");
    private static readonly Guid OtherOwnerId = Id("owner-2");

    private static MongoDbContainer? mongoDbContainer;

    private IMongoCollection<BsonDocument> collection = null!;
    private GetTodosQueryHandler handler = null!;
    private ServiceProvider? serviceProvider;

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
                "Set RUN_MONGODB_INTEGRATION_TESTS=true and start Docker to run MongoDB list tests.");
        }

        string databaseName = $"sleekyTodoListTests_{Guid.NewGuid():N}";
        IMongoDatabase database = new MongoClient(mongoDbContainer.GetConnectionString())
            .GetDatabase(databaseName);
        collection = database.GetCollection<BsonDocument>("todoItems");
        handler = new GetTodosQueryHandler(ResolveListReader(databaseName));
    }

    [TestCleanup]
    public void TestCleanup()
    {
        serviceProvider?.Dispose();
        serviceProvider = null;
    }

    [TestMethod]
    public async Task DefaultFirstPageReturnsFiftyItems()
    {
        await SeedAsync(
            Enumerable.Range(1, 55)
                .Select(index => CreateDocument(
                    $"todo-{index:D3}",
                    $"TODO {index:D3}",
                    new DateOnly(2026, 8, 1).AddDays(index / 4)))
                .ToArray());

        CursorPage<TodoListItemDto> page = await ListAsync(new GetTodosQuery());

        page.Items.Should().HaveCount(50);
        page.NextCursor.Should().NotBeNullOrWhiteSpace();
        page.Items.Select(item => item.Id).Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public async Task StatusPriorityAndDueDateFiltersAreApplied()
    {
        await SeedAsync(
            CreateDocument(
                "todo-a",
                "Alpha",
                new DateOnly(2026, 8, 10),
                TodoStatus.NotStarted,
                TodoPriority.Low),
            CreateDocument(
                "todo-b",
                "Bravo",
                new DateOnly(2026, 8, 20),
                TodoStatus.Completed,
                TodoPriority.High),
            CreateDocument(
                "todo-c",
                "Charlie",
                new DateOnly(2026, 9, 1),
                TodoStatus.Completed,
                TodoPriority.Medium));

        CursorPage<TodoListItemDto> statusPage = await ListAsync(
            new GetTodosQuery(status: TodoStatus.Completed));
        CursorPage<TodoListItemDto> priorityPage = await ListAsync(
            new GetTodosQuery(priority: TodoPriority.High));
        CursorPage<TodoListItemDto> duePage = await ListAsync(
            new GetTodosQuery(
                dueFrom: new DateOnly(2026, 8, 15),
                dueTo: new DateOnly(2026, 8, 31)));

        statusPage.Items.Select(item => item.Id)
            .Should().BeEquivalentTo(new[] { Id("todo-b"), Id("todo-c") });
        priorityPage.Items.Select(item => item.Id).Should().Equal(Id("todo-b"));
        duePage.Items.Select(item => item.Id).Should().Equal(Id("todo-b"));
    }

    [TestMethod]
    [DataRow("DueDate", "Asc")]
    [DataRow("DueDate", "Desc")]
    [DataRow("Priority", "Asc")]
    [DataRow("Priority", "Desc")]
    [DataRow("Status", "Asc")]
    [DataRow("Status", "Desc")]
    [DataRow("Name", "Asc")]
    [DataRow("Name", "Desc")]
    public async Task SortsAndPaginatesWithoutDuplicates(
        string sortFieldName,
        string directionName)
    {
        BsonDocument[] documents =
        [
            CreateDocument(
                "todo-a",
                "Delta",
                new DateOnly(2026, 8, 12),
                TodoStatus.Completed,
                TodoPriority.High),
            CreateDocument(
                "todo-b",
                "alpha",
                new DateOnly(2026, 8, 10),
                TodoStatus.NotStarted,
                TodoPriority.Low),
            CreateDocument(
                "todo-c",
                "Charlie",
                new DateOnly(2026, 8, 10),
                TodoStatus.InProgress,
                TodoPriority.Medium),
            CreateDocument(
                "todo-d",
                "bravo",
                new DateOnly(2026, 8, 11),
                TodoStatus.NotStarted,
                TodoPriority.High),
            CreateDocument(
                "todo-e",
                "Echo",
                new DateOnly(2026, 8, 11),
                TodoStatus.Completed,
                TodoPriority.Low),
            CreateDocument(
                "todo-f",
                "foxtrot",
                new DateOnly(2026, 8, 12),
                TodoStatus.InProgress,
                TodoPriority.Medium),
            CreateDocument(
                "todo-g",
                "Golf",
                new DateOnly(2026, 8, 10),
                TodoStatus.NotStarted,
                TodoPriority.High),
        ];
        await SeedAsync(documents);
        TodoSortField sortField = Enum.Parse<TodoSortField>(sortFieldName);
        TodoSortDirection direction = Enum.Parse<TodoSortDirection>(directionName);
        List<TodoListItemDto> allItems = new List<TodoListItemDto>();
        string? cursor = null;

        do
        {
            CursorPage<TodoListItemDto> page = await ListAsync(
                new GetTodosQuery(
                    sortField: sortField,
                    sortDirection: direction,
                    limit: 2,
                    cursor: cursor));
            allItems.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Guid[] expectedIds = GetExpectedIds(documents, sortField, direction);
        allItems.Select(item => item.Id).Should().Equal(expectedIds);
        allItems.Should().HaveCount(documents.Length);
        allItems.Select(item => item.Id).Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public async Task ActiveArchivedAndDeletedScopesAreSeparated()
    {
        await SeedAsync(
            CreateDocument("active", "Active", new DateOnly(2026, 8, 1)),
            CreateDocument(
                "archived",
                "Archived",
                new DateOnly(2026, 8, 2),
                TodoStatus.Archived),
            CreateDocument(
                "deleted",
                "Deleted",
                new DateOnly(2026, 8, 3),
                deleted: true));

        CursorPage<TodoListItemDto> active = await ListAsync(
            new GetTodosQuery(scope: TodoListScope.Active));
        CursorPage<TodoListItemDto> archived = await ListAsync(
            new GetTodosQuery(scope: TodoListScope.Archived));
        CursorPage<TodoListItemDto> deleted = await ListAsync(
            new GetTodosQuery(scope: TodoListScope.Deleted));

        active.Items.Select(item => item.Id).Should().Equal(Id("active"));
        archived.Items.Select(item => item.Id).Should().Equal(Id("archived"));
        deleted.Items.Select(item => item.Id).Should().Equal(Id("deleted"));
        deleted.Items[0].DeletedAt.Should().NotBeNull();
        deleted.Items[0].PurgeAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task BlockedStateIsCalculatedBeforeFilteringAndPagination()
    {
        await SeedAsync(
            CreateDocument(
                "dependency-open",
                "Open dependency",
                new DateOnly(2026, 8, 1)),
            CreateDocument(
                "dependency-complete",
                "Complete dependency",
                new DateOnly(2026, 8, 1),
                TodoStatus.Completed),
            CreateDocument(
                "dependency-archived",
                "Archived dependency",
                new DateOnly(2026, 8, 1),
                TodoStatus.Archived),
            CreateDocument(
                "dependency-deleted",
                "Deleted dependency",
                new DateOnly(2026, 8, 1),
                deleted: true),
            CreateDocument(
                "dependent-blocked",
                "Blocked",
                new DateOnly(2026, 8, 2),
                dependencies: new[] { "dependency-open", "dependency-missing" }),
            CreateDocument(
                "dependent-archived-dependency",
                "Blocked by archived",
                new DateOnly(2026, 8, 2),
                dependencies: new[] { "dependency-archived" }),
            CreateDocument(
                "dependent-deleted-dependency",
                "Blocked by deleted",
                new DateOnly(2026, 8, 2),
                dependencies: new[] { "dependency-deleted" }),
            CreateDocument(
                "dependent-unblocked",
                "Unblocked",
                new DateOnly(2026, 8, 3),
                dependencies: new[] { "dependency-complete" }));

        CursorPage<TodoListItemDto> blocked = await ListAsync(
            new GetTodosQuery(dependencyStatus: TodoDependencyStatus.Blocked));
        CursorPage<TodoListItemDto> unblocked = await ListAsync(
            new GetTodosQuery(dependencyStatus: TodoDependencyStatus.Unblocked));

        blocked.Items.Select(item => item.Id).Should().BeEquivalentTo(
            new[]
            {
                Id("dependent-blocked"),
                Id("dependent-archived-dependency"),
                Id("dependent-deleted-dependency"),
            });
        blocked.Items.Single(item => item.Id == Id("dependent-blocked"))
            .IncompleteDependencyCount.Should().Be(2);
        blocked.Items.Should().OnlyContain(item => item.IsBlocked);
        unblocked.Items.Select(item => item.Id).Should().Contain(Id("dependent-unblocked"));
        unblocked.Items.Single(item => item.Id == Id("dependent-unblocked"))
            .IncompleteDependencyCount.Should().Be(0);

        List<TodoListItemDto> pagedBlockedItems = new List<TodoListItemDto>();
        string? cursor = null;
        do
        {
            CursorPage<TodoListItemDto> page = await ListAsync(
                new GetTodosQuery(
                    dependencyStatus: TodoDependencyStatus.Blocked,
                    limit: 1,
                    cursor: cursor));
            pagedBlockedItems.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        pagedBlockedItems.Select(item => item.Id).Should().BeEquivalentTo(
            new[]
            {
                Id("dependent-blocked"),
                Id("dependent-archived-dependency"),
                Id("dependent-deleted-dependency"),
            });
        pagedBlockedItems.Select(item => item.Id).Should().OnlyHaveUniqueItems();
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static BsonDocument CreateDocument(
        string id,
        string name,
        DateOnly dueDate,
        TodoStatus status = TodoStatus.NotStarted,
        TodoPriority priority = TodoPriority.Medium,
        IReadOnlyList<string>? dependencies = null,
        bool deleted = false,
        Guid? ownerId = null)
    {
        DateTime timestamp = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        BsonValue deletedAt = deleted ? new BsonDateTime(timestamp) : BsonNull.Value;
        BsonValue purgeAt = deleted
            ? new BsonDateTime(timestamp.AddDays(90))
            : BsonNull.Value;

        return new BsonDocument
        {
            {
                "_id",
                new BsonBinaryData(Id(id), GuidRepresentation.Standard)
            },
            {
                "ownerId",
                new BsonBinaryData(ownerId ?? OwnerId, GuidRepresentation.Standard)
            },
            { "name", name },
            { "nameNormalized", name.ToLowerInvariant() },
            { "description", $"Description for {name}" },
            { "dueDate", dueDate.ToString("yyyy-MM-dd") },
            { "status", (int)status },
            { "priority", (int)priority },
            {
                "dependencyIds",
                new BsonArray(
                    (dependencies ?? Array.Empty<string>())
                        .Select(dependency => new BsonBinaryData(
                            Id(dependency),
                            GuidRepresentation.Standard)))
            },
            { "recurrence", BsonNull.Value },
            { "seriesId", BsonNull.Value },
            { "occurrenceNumber", BsonNull.Value },
            { "version", 1L },
            { "createdAt", timestamp },
            { "updatedAt", timestamp },
            { "deletedAt", deletedAt },
            { "purgeAt", purgeAt },
        };
    }

    private static Guid[] GetExpectedIds(
        IReadOnlyList<BsonDocument> documents,
        TodoSortField sortField,
        TodoSortDirection direction)
    {
        Func<BsonDocument, IComparable> value = sortField switch
        {
            TodoSortField.DueDate => document => document["dueDate"].AsString,
            TodoSortField.Priority => document => document["priority"].AsInt32,
            TodoSortField.Status => document => document["status"].AsInt32,
            TodoSortField.Name => document => document["nameNormalized"].AsString,
            _ => throw new ArgumentOutOfRangeException(nameof(sortField)),
        };
        IEnumerable<BsonDocument> ordered = direction == TodoSortDirection.Asc
            ? documents.OrderBy(value).ThenBy(GetIdString)
            : documents.OrderByDescending(value)
                .ThenByDescending(GetIdString);

        return ordered
            .Select(document => document["_id"].AsBsonBinaryData.ToGuid())
            .ToArray();
    }

    private static string GetIdString(BsonDocument document)
    {
        return document["_id"].AsBsonBinaryData.ToGuid().ToString("D");
    }

    private static Guid Id(string value)
    {
        byte[] bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }

    /// <summary>
    /// The Mongo implementations are internal to the infrastructure assembly, so
    /// the suite resolves the reader contract from the real registration rather
    /// than constructing the concrete type.
    /// </summary>
    private ITodoListReader ResolveListReader(string databaseName)
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            [$"{MongoDbSettings.SectionName}:ConnectionString"] =
                mongoDbContainer!.GetConnectionString(),
            [$"{MongoDbSettings.SectionName}:DatabaseName"] = databaseName,
            [$"{MongoDbSettings.SectionName}:TodoItemsCollectionName"] = "todoItems",
        };
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new TestCurrentUser(OwnerId));
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build());

        serviceProvider = services.BuildServiceProvider();

        return serviceProvider.GetRequiredService<ITodoListReader>();
    }

    private async Task<CursorPage<TodoListItemDto>> ListAsync(GetTodosQuery query)
    {
        return await handler.Handle(query, CancellationToken.None);
    }

    private async Task SeedAsync(params BsonDocument[] documents)
    {
        await collection.InsertManyAsync(documents);
    }
}
