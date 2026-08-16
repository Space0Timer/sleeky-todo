using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Services;
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

    private readonly List<BsonDocument> aggregateCommands = new List<BsonDocument>();

    private IMongoCollection<BsonDocument> collection = null!;
    private IMongoDatabase database = null!;
    private GetTodosQueryHandler handler = null!;
    private ServiceProvider? serviceProvider;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext testContext)
    {
        if (!ShouldRunMongoDbTests())
        {
            return;
        }

        mongoDbContainer = new MongoDbBuilder("mongo:8.0").Build();
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
        this.database = database;
        aggregateCommands.Clear();
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
                TodoStatus.Open,
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
                TodoStatus.Open,
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
                TodoStatus.Open,
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
                TodoStatus.Open,
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

    /// <summary>
    /// The description is cut to the preview length on the server now, so these
    /// pin the boundary the cut has to preserve. A card never renders more than
    /// the preview, and sending a full two-thousand-character description to
    /// build one was most of a page's payload.
    /// </summary>
    [TestMethod]
    [DataRow(50, DisplayName = "well under the preview length")]
    [DataRow(119, DisplayName = "one under")]
    [DataRow(120, DisplayName = "exactly the preview length")]
    public async Task ADescriptionWithinThePreviewLengthIsReturnedWhole(int length)
    {
        string description = new string('x', length);
        await SeedAsync(CreateDocument(
            "preview",
            "Preview",
            new DateOnly(2026, 8, 1),
            description: description));

        CursorPage<TodoListItemDto> page = await ListAsync(new GetTodosQuery());

        page.Items.Single().DescriptionPreview.Should().Be(description);
    }

    [TestMethod]
    [DataRow(121, DisplayName = "one over")]
    [DataRow(2000, DisplayName = "the maximum a description may hold")]
    public async Task ALongerDescriptionIsTruncatedWithAnEllipsis(int length)
    {
        await SeedAsync(CreateDocument(
            "preview",
            "Preview",
            new DateOnly(2026, 8, 1),
            description: new string('x', length)));

        CursorPage<TodoListItemDto> page = await ListAsync(new GetTodosQuery());

        string? preview = page.Items.Single().DescriptionPreview;
        preview.Should().Be(new string('x', 117) + "...");
        preview.Should().HaveLength(120);
    }

    /// <summary>
    /// Cut by code point rather than by byte, so a character encoded in more
    /// than one byte never arrives as half of itself.
    /// </summary>
    [TestMethod]
    public async Task AMultiByteDescriptionIsNotSplitMidCharacter()
    {
        await SeedAsync(CreateDocument(
            "preview",
            "Preview",
            new DateOnly(2026, 8, 1),
            description: new string('日', 400)));

        CursorPage<TodoListItemDto> page = await ListAsync(new GetTodosQuery());

        page.Items.Single().DescriptionPreview
            .Should().Be(new string('日', 117) + "...");
    }

    [TestMethod]
    public async Task AnAbsentDescriptionStaysNullRatherThanBecomingEmpty()
    {
        BsonDocument document = CreateDocument(
            "preview",
            "Preview",
            new DateOnly(2026, 8, 1));
        document["description"] = BsonNull.Value;
        await SeedAsync(document);

        CursorPage<TodoListItemDto> page = await ListAsync(new GetTodosQuery());

        page.Items.Single().DescriptionPreview.Should().BeNull();
    }

    [TestMethod]
    public async Task SearchMatchesATokenOfTheNameByPrefix()
    {
        await SeedSearchCorpusAsync();

        CursorPage<TodoListItemDto> page = await ListAsync(
            new GetTodosQuery(searchText: "mil"));

        page.Items.Select(item => item.Id).Should().BeEquivalentTo(
            new[] { Id("milk"), Id("milkshake") });
    }

    /// <summary>
    /// A prefix, not a substring: the middle of a word does not match. This is
    /// the semantic change from the client-side filter the picker used to
    /// apply, and it is what makes the match index-backed.
    /// </summary>
    [TestMethod]
    public async Task SearchDoesNotMatchTheMiddleOfAToken()
    {
        await SeedSearchCorpusAsync();

        CursorPage<TodoListItemDto> page = await ListAsync(
            new GetTodosQuery(searchText: "ilk"));

        page.Items.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SearchMatchesATokenOfTheDescription()
    {
        await SeedSearchCorpusAsync();

        CursorPage<TodoListItemDto> page = await ListAsync(
            new GetTodosQuery(searchText: "semiskimmed"));

        page.Items.Select(item => item.Id).Should().Equal(Id("milk"));
    }

    [TestMethod]
    public async Task MultipleTermsMustAllMatchAndMaySpanNameAndDescription()
    {
        await SeedSearchCorpusAsync();

        CursorPage<TodoListItemDto> both = await ListAsync(
            new GetTodosQuery(searchText: "milk semi"));
        CursorPage<TodoListItemDto> unmatched = await ListAsync(
            new GetTodosQuery(searchText: "milk bicycle"));

        both.Items.Select(item => item.Id).Should().Equal(Id("milk"));
        unmatched.Items.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SearchIgnoresTheCaseAndPunctuationOfWhatWasTyped()
    {
        await SeedSearchCorpusAsync();

        CursorPage<TodoListItemDto> typed = await ListAsync(
            new GetTodosQuery(searchText: "  BUY,  MIL!  "));
        CursorPage<TodoListItemDto> canonical = await ListAsync(
            new GetTodosQuery(searchText: "buy mil"));

        typed.Items.Select(item => item.Id).Should().Equal(Id("milk"));
        typed.Items.Select(item => item.Id).Should().Equal(
            canonical.Items.Select(item => item.Id));
    }

    [TestMethod]
    public async Task PunctuationOnlySearchTextLeavesTheListUnfiltered()
    {
        await SeedSearchCorpusAsync();

        CursorPage<TodoListItemDto> page = await ListAsync(
            new GetTodosQuery(searchText: "!!! ---"));

        page.Items.Should().HaveCount(4);
    }

    [TestMethod]
    public async Task SearchDoesNotReachAnotherOwnersTodos()
    {
        await SeedSearchCorpusAsync();
        await SeedAsync(CreateDocument(
            "other-owner-milk",
            "Buy milk",
            new DateOnly(2026, 8, 1),
            ownerId: OtherOwnerId));

        CursorPage<TodoListItemDto> page = await ListAsync(
            new GetTodosQuery(searchText: "milk"));

        page.Items.Select(item => item.Id).Should().BeEquivalentTo(
            new[] { Id("milk"), Id("milkshake") });
        page.Items.Select(item => item.Id).Should().NotContain(Id("other-owner-milk"));
    }

    [TestMethod]
    public async Task SearchAppliesWithinTheSelectedScope()
    {
        await SeedSearchCorpusAsync();

        CursorPage<TodoListItemDto> active = await ListAsync(
            new GetTodosQuery(searchText: "archived"));
        CursorPage<TodoListItemDto> archived = await ListAsync(
            new GetTodosQuery(scope: TodoListScope.Archived, searchText: "archived"));
        CursorPage<TodoListItemDto> trash = await ListAsync(
            new GetTodosQuery(scope: TodoListScope.Deleted, searchText: "deleted"));

        active.Items.Should().BeEmpty();
        archived.Items.Select(item => item.Id).Should().Equal(Id("archived-milk"));
        trash.Items.Select(item => item.Id).Should().Equal(Id("deleted-milk"));
    }

    [TestMethod]
    public async Task SearchPagesThroughASecondKeysetPageWithoutGapsOrDuplicates()
    {
        await SeedAsync(Enumerable
            .Range(1, 7)
            .Select(index => CreateDocument(
                $"page-{index:D2}",
                $"Grocery run {index:D2}",
                new DateOnly(2026, 8, 1).AddDays(index)))
            .Append(CreateDocument("excluded", "Unrelated", new DateOnly(2026, 8, 1)))
            .ToArray());

        List<Guid> collected = new List<Guid>();
        string? cursor = null;
        do
        {
            CursorPage<TodoListItemDto> page = await ListAsync(
                new GetTodosQuery(searchText: "grocery", limit: 3, cursor: cursor));
            collected.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        collected.Should().OnlyHaveUniqueItems();
        collected.Should().BeEquivalentTo(Enumerable
            .Range(1, 7)
            .Select(index => Id($"page-{index:D2}")));
    }

    /// <summary>
    /// Asserts the bounds rather than the index name.
    /// </summary>
    /// <remarks>
    /// Under a hint the winning plan names the search index whatever happens,
    /// including when the token bounds collapse to the whole key range and the
    /// query degenerates into the scan this feature exists to avoid. What has
    /// to hold is that the owner and scope are matched exactly and the first
    /// term is answered as a prefix range, so those are what is measured. The
    /// non-ASCII term is here because a prefix range is built by incrementing
    /// the last character, which is not an ASCII-only operation.
    /// </remarks>
    [TestMethod]
    public async Task ASearchingQueryIsAnsweredFromPrefixBoundsOnTheSearchIndex()
    {
        await SeedSearchCorpusAsync();

        BsonDocument plan = await ExplainSearchAsync("café milk");
        BsonDocument bounds = plan["indexBounds"].AsBsonDocument;

        plan["indexName"].AsString.Should().Be("owner_active_search_tokens");
        ReadBound(bounds, "ownerId").Should().ContainSingle()
            .Which.Should().MatchRegex(@"^\[.+, .+\]$");

        // One exact point interval. A null match also covers a document where
        // the field is absent, and the planner folds that case into the null
        // bound rather than opening a second interval for it. What matters is
        // that the bound stays an exact value and does not open the key range.
        ReadBound(bounds, "deletedAt").Should().Equal("[null, null]");

        // The longest term leads, and its bound is the half-open interval a
        // prefix search produces rather than the whole key range. The upper
        // edge is the last character incremented — ê follows é — which is what
        // proves the prefix range holds beyond ASCII. The second interval is
        // how a regex predicate reaches values stored as regexes; both are
        // tight, and neither is [MinKey, MaxKey].
        ReadBound(bounds, "searchTokens").Should().Equal(
            "[\"café\", \"cafê\")",
            "[/^café/, /^café/]");
    }

    /// <summary>
    /// A query with nothing to search for is left to the planner exactly as it
    /// was before search existed: no hint is sent, and it settles on one of the
    /// owner-scoped sort indexes rather than the search index. Which one it
    /// picks is the planner's call and is deliberately not asserted.
    /// </summary>
    [TestMethod]
    public async Task AQueryWithoutSearchTextIsLeftToThePlanner()
    {
        await SeedSearchCorpusAsync();

        BsonDocument plan = await ExplainListAsync(new GetTodosQuery());

        aggregateCommands[^1].Contains("hint").Should().BeFalse();
        plan["indexName"].AsString.Should().NotBe("owner_active_search_tokens");
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
        TodoStatus status = TodoStatus.Open,
        TodoPriority priority = TodoPriority.Medium,
        IReadOnlyList<string>? dependencies = null,
        bool deleted = false,
        Guid? ownerId = null,
        string? description = null)
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
            { "description", description ?? $"Description for {name}" },

            // Search reads the stored tokens rather than the text, so a
            // fixture without them is invisible to every search assertion.
            {
                "searchTokens",
                new BsonArray(SearchTokenizer.Tokenize(
                    name,
                    description ?? $"Description for {name}"))
            },
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
    /// The index scan sits at a different depth depending on how much of the
    /// pipeline the server pushes down, so it is found by shape rather than by
    /// a path that a server upgrade could move.
    /// </summary>
    private static BsonDocument? FindIndexScan(BsonValue value)
    {
        if (value is BsonArray array)
        {
            return array.Select(FindIndexScan).FirstOrDefault(found => found is not null);
        }

        if (value is not BsonDocument document)
        {
            return null;
        }

        if (document.Contains("indexName") && document.Contains("indexBounds"))
        {
            return document;
        }

        return document.Values
            .Select(FindIndexScan)
            .FirstOrDefault(found => found is not null);
    }

    private static string[] ReadBound(BsonDocument bounds, string field)
    {
        return bounds[field].AsBsonArray
            .Select(interval => interval.AsString)
            .ToArray();
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
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new TestCurrentUser(OwnerId));
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build());

        // Registered after the infrastructure's own client so this one is
        // resolved. It behaves identically apart from recording the commands it
        // sends, which is how the plan assertions reach the pipeline the reader
        // actually built rather than a copy of it.
        services.AddSingleton<IMongoClient>(_ => CreateCommandCapturingClient());

        serviceProvider = services.BuildServiceProvider();

        // A searching query hints the search index by name, which fails rather
        // than degrades when the index is absent. The host builds it through
        // the same hosted service before serving a request, so the fixture has
        // to as well.
        foreach (IHostedService hostedService in
            serviceProvider.GetServices<IHostedService>())
        {
            hostedService.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return serviceProvider.GetRequiredService<ITodoListReader>();
    }

    private IMongoClient CreateCommandCapturingClient()
    {
        MongoClientSettings clientSettings = MongoClientSettings.FromConnectionString(
            mongoDbContainer!.GetConnectionString());
        clientSettings.ClusterConfigurator = builder => builder.Subscribe<CommandStartedEvent>(
            started =>
            {
                if (!string.Equals(started.CommandName, "aggregate", StringComparison.Ordinal))
                {
                    return;
                }

                // The driver reuses the event's buffer, so the command is
                // copied before it leaves the callback.
                lock (aggregateCommands)
                {
                    aggregateCommands.Add(started.Command.DeepClone().AsBsonDocument);
                }
            });

        return new MongoClient(clientSettings);
    }

    private async Task<CursorPage<TodoListItemDto>> ListAsync(GetTodosQuery query)
    {
        return await handler.Handle(query, CancellationToken.None);
    }

    private async Task SeedAsync(params BsonDocument[] documents)
    {
        await collection.InsertManyAsync(documents);
    }

    /// <summary>
    /// One corpus for the search assertions: two names sharing a prefix, a
    /// description-only match, and one TODO on each of the other two shelves.
    /// </summary>
    private async Task SeedSearchCorpusAsync()
    {
        await SeedAsync(
            CreateDocument(
                "milk",
                "Buy milk",
                new DateOnly(2026, 8, 1),
                description: "Semiskimmed from the café"),
            CreateDocument(
                "milkshake",
                "Blend a milkshake",
                new DateOnly(2026, 8, 2),
                description: "Weekend treat"),
            CreateDocument(
                "bread",
                "Buy bread",
                new DateOnly(2026, 8, 3),
                description: "Sourdough"),
            CreateDocument(
                "unrelated",
                "Book a haircut",
                new DateOnly(2026, 8, 4),
                description: "Any afternoon"),
            CreateDocument(
                "archived-milk",
                "Archived milk order",
                new DateOnly(2026, 8, 5),
                TodoStatus.Archived),
            CreateDocument(
                "deleted-milk",
                "Deleted milk order",
                new DateOnly(2026, 8, 6),
                deleted: true));
    }

    private async Task<BsonDocument> ExplainSearchAsync(string searchText)
    {
        _ = await ListAsync(new GetTodosQuery(searchText: searchText));

        return await ExplainLastAggregateAsync();
    }

    private async Task<BsonDocument> ExplainListAsync(GetTodosQuery query)
    {
        _ = await ListAsync(query);

        return await ExplainLastAggregateAsync();
    }

    /// <summary>
    /// Replays the command the reader actually sent rather than a copy of the
    /// pipeline written here, so the plan measured is the one production runs
    /// and cannot drift as the reader changes.
    /// </summary>
    private async Task<BsonDocument> ExplainLastAggregateAsync()
    {
        BsonDocument sent = aggregateCommands.Count > 0
            ? aggregateCommands[^1]
            : throw new InvalidOperationException(
                "The list query should have issued an aggregate command.");
        BsonDocument explainable = new BsonDocument(sent.Elements
            .Where(element => element.Name
                is "aggregate" or "pipeline" or "cursor" or "hint"));

        BsonDocument explained = await database.RunCommandAsync<BsonDocument>(
            new BsonDocument
            {
                { "explain", explainable },
                { "verbosity", "queryPlanner" },
            });

        return FindIndexScan(explained)
            ?? throw new InvalidOperationException(
                $"The plan used no index: {explained.ToJson()}");
    }
}
