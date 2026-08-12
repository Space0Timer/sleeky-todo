using FluentAssertions;

using Microsoft.Extensions.Options;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence;
using Sleeky.Todo.Infrastructure.Persistence.Repositories;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Persistence;

[TestClass]
public sealed class MongoTodoRepositoryTests
{
    private static readonly DateTimeOffset Timestamp = new DateTimeOffset(
        2026,
        8,
        12,
        1,
        0,
        0,
        TimeSpan.Zero);

    private static MongoDbContainer? mongoDbContainer;

    private IMongoDatabase database = null!;
    private ITodoRepository repository = null!;

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
                "Set RUN_MONGODB_INTEGRATION_TESTS=true and start Docker to run MongoDB repository tests.");
        }

        MongoClient client = new MongoClient(mongoDbContainer.GetConnectionString());
        string databaseName = $"sleekyTodoTests_{Guid.NewGuid():N}";
        database = client.GetDatabase(databaseName);
        MongoDbSettings settings = new MongoDbSettings
        {
            ConnectionString = mongoDbContainer.GetConnectionString(),
            DatabaseName = databaseName,
            TodoItemsCollectionName = "todoItems",
        };
        repository = new MongoTodoRepository(database, Options.Create(settings));
    }

    [TestMethod]
    public async Task AddAndGetRoundTripsThroughRepositoryContract()
    {
        TodoItem todoItem = CreateTodo();

        await repository.AddAsync(todoItem);
        TodoItem? storedTodo = await repository.GetByIdAsync(todoItem.Id);

        storedTodo.Should().NotBeNull();
        storedTodo!.Id.Should().Be(todoItem.Id);
        storedTodo.Name.Should().Be(todoItem.Name);
        storedTodo.NameNormalized.Should().Be(todoItem.NameNormalized);
        storedTodo.Description.Should().Be(todoItem.Description);
        storedTodo.DueDate.Should().Be(todoItem.DueDate);
        storedTodo.Status.Should().Be(todoItem.Status);
        storedTodo.Priority.Should().Be(todoItem.Priority);
        storedTodo.Version.Should().Be(todoItem.Version);
        storedTodo.CreatedAt.Should().Be(todoItem.CreatedAt);
        storedTodo.UpdatedAt.Should().Be(todoItem.UpdatedAt);
    }

    [TestMethod]
    public async Task DeletedTodoIsOnlyReturnedWhenExplicitlyIncluded()
    {
        TodoItem todoItem = CreateTodo();
        todoItem.SoftDelete(Timestamp.AddDays(1));
        await repository.AddAsync(todoItem);

        TodoItem? activeResult = await repository.GetByIdAsync(todoItem.Id);
        TodoItem? deletedResult = await repository.GetByIdAsync(
            todoItem.Id,
            includeDeleted: true);

        activeResult.Should().BeNull();
        deletedResult.Should().NotBeNull();
        deletedResult!.DeletedAt.Should().Be(todoItem.DeletedAt);
        deletedResult.PurgeAt.Should().Be(todoItem.PurgeAt);
    }

    [TestMethod]
    public async Task RepositoryWritesExpectedBsonRepresentations()
    {
        TodoItem todoItem = CreateTodo();
        await repository.AddAsync(todoItem);
        IMongoCollection<BsonDocument> collection = database
            .GetCollection<BsonDocument>("todoItems");

        BsonDocument document = await collection
            .Find(Builders<BsonDocument>.Filter.Eq("_id", todoItem.Id))
            .SingleAsync();

        document["dueDate"].AsString.Should().Be("2026-08-31");
        document["status"].AsString.Should().Be(nameof(TodoStatus.NotStarted));
        document["priority"].AsString.Should().Be(nameof(TodoPriority.High));
        document["createdAt"].BsonType.Should().Be(BsonType.DateTime);
    }

    private static TodoItem CreateTodo()
    {
        return TodoItem.Create(
            "todo-1",
            "Submit report",
            "Monthly report",
            new DateOnly(2026, 8, 31),
            TodoPriority.High,
            Timestamp);
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
