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

    [TestMethod]
    public async Task ConcurrentUpdatesWithSameVersionAllowExactlyOneWriter()
    {
        TodoItem todoItem = CreateTodo();
        await repository.AddAsync(todoItem);
        TodoItem firstWriter = await GetRequiredTodoAsync(todoItem.Id);
        TodoItem secondWriter = await GetRequiredTodoAsync(todoItem.Id);
        firstWriter.UpdateDetails(
            "First writer",
            null,
            todoItem.DueDate,
            TodoPriority.Low,
            Timestamp.AddHours(1));
        secondWriter.UpdateDetails(
            "Second writer",
            null,
            todoItem.DueDate,
            TodoPriority.Medium,
            Timestamp.AddHours(2));

        TodoItem?[] results = await Task.WhenAll(
            repository.UpdateAsync(firstWriter, todoItem.Version),
            repository.UpdateAsync(secondWriter, todoItem.Version));

        results.Count(result => result is not null).Should().Be(1);
        results.Count(result => result is null).Should().Be(1);
        TodoItem winningResult = results.Single(result => result is not null)
            ?? throw new InvalidOperationException("A concurrent update should have succeeded.");
        winningResult.Version.Should().Be(2);
        TodoItem persistedTodo = await GetRequiredTodoAsync(todoItem.Id);
        persistedTodo.Version.Should().Be(2);
        persistedTodo.Name.Should().BeOneOf("First writer", "Second writer");
    }

    [TestMethod]
    public async Task ConcurrentUpdateAndDeleteWithSameVersionAllowExactlyOneMutation()
    {
        TodoItem todoItem = CreateTodo();
        await repository.AddAsync(todoItem);
        TodoItem updateWriter = await GetRequiredTodoAsync(todoItem.Id);
        TodoItem deleteWriter = await GetRequiredTodoAsync(todoItem.Id);
        updateWriter.UpdateDetails(
            "Updated name",
            todoItem.Description,
            todoItem.DueDate,
            todoItem.Priority,
            Timestamp.AddHours(1));
        deleteWriter.SoftDelete(Timestamp.AddHours(2));

        Task<TodoItem?> updateTask = repository.UpdateAsync(
            updateWriter,
            todoItem.Version);
        Task<TodoItem?> deleteTask = repository.SoftDeleteAsync(
            deleteWriter,
            todoItem.Version);
        TodoItem?[] results = await Task.WhenAll(updateTask, deleteTask);

        results.Count(result => result is not null).Should().Be(1);
        results.Count(result => result is null).Should().Be(1);
        TodoItem persistedTodo = await GetRequiredTodoAsync(
            todoItem.Id,
            includeDeleted: true);
        persistedTodo.Version.Should().Be(2);
        if (updateTask.Result is not null)
        {
            persistedTodo.Name.Should().Be("Updated name");
            persistedTodo.DeletedAt.Should().BeNull();
        }
        else
        {
            persistedTodo.DeletedAt.Should().NotBeNull();
        }
    }

    [TestMethod]
    public async Task ConcurrentRestoresWithSameVersionAllowExactlyOneWriter()
    {
        TodoItem todoItem = CreateTodo();
        await repository.AddAsync(todoItem);
        TodoItem deleteWriter = await GetRequiredTodoAsync(todoItem.Id);
        deleteWriter.SoftDelete(Timestamp.AddHours(1));
        TodoItem deletedTodo = await repository.SoftDeleteAsync(
            deleteWriter,
            todoItem.Version)
            ?? throw new InvalidOperationException("The setup delete should have succeeded.");
        TodoItem firstWriter = await GetRequiredTodoAsync(
            todoItem.Id,
            includeDeleted: true);
        TodoItem secondWriter = await GetRequiredTodoAsync(
            todoItem.Id,
            includeDeleted: true);
        firstWriter.Restore(Timestamp.AddHours(2));
        secondWriter.Restore(Timestamp.AddHours(3));

        TodoItem?[] results = await Task.WhenAll(
            repository.RestoreAsync(firstWriter, deletedTodo.Version),
            repository.RestoreAsync(secondWriter, deletedTodo.Version));

        results.Count(result => result is not null).Should().Be(1);
        results.Count(result => result is null).Should().Be(1);
        TodoItem persistedTodo = await GetRequiredTodoAsync(todoItem.Id);
        persistedTodo.Version.Should().Be(3);
        persistedTodo.DeletedAt.Should().BeNull();
        persistedTodo.PurgeAt.Should().BeNull();
    }

    [TestMethod]
    public async Task MutationRejectsDomainObjectAtDifferentVersion()
    {
        TodoItem todoItem = CreateTodo();
        await repository.AddAsync(todoItem);
        TodoItem persistedVersionTwo = TodoItem.Rehydrate(
            todoItem.Id,
            todoItem.Name,
            todoItem.Description,
            todoItem.DueDate,
            todoItem.Status,
            todoItem.Priority,
            todoItem.DependencyIds,
            todoItem.Recurrence,
            todoItem.SeriesId,
            todoItem.OccurrenceNumber,
            2,
            todoItem.CreatedAt,
            todoItem.UpdatedAt,
            todoItem.DeletedAt,
            todoItem.PurgeAt);

        TodoItem? result = await repository.UpdateAsync(
            persistedVersionTwo,
            expectedVersion: 1);

        result.Should().BeNull();
        TodoItem storedTodo = await GetRequiredTodoAsync(todoItem.Id);
        storedTodo.Version.Should().Be(1);
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

    private async Task<TodoItem> GetRequiredTodoAsync(
        string id,
        bool includeDeleted = false)
    {
        return await repository.GetByIdAsync(id, includeDeleted)
            ?? throw new InvalidOperationException($"TODO '{id}' should exist for the test.");
    }
}
