using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.ValueObjects;
using Sleeky.Todo.Infrastructure.DependencyInjection;
using Sleeky.Todo.Infrastructure.Persistence;

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

    private static readonly Guid OwnerId = Id("owner-1");
    private static readonly Guid OtherOwnerId = Id("owner-2");

    private static MongoDbContainer? mongoDbContainer;

    private readonly List<ServiceProvider> providers = new List<ServiceProvider>();

    private IMongoDatabase database = null!;
    private ITodoRepository repository = null!;
    private ITodoRepository otherOwnerRepository = null!;
    private MongoDbSettings settings = null!;

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
        settings = new MongoDbSettings
        {
            ConnectionString = mongoDbContainer.GetConnectionString(),
            DatabaseName = databaseName,
            TodoItemsCollectionName = "todoItems",
        };
        repository = CreateRepository(OwnerId);
        otherOwnerRepository = CreateRepository(OtherOwnerId);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        foreach (ServiceProvider serviceProvider in providers)
        {
            serviceProvider.Dispose();
        }

        providers.Clear();
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
    public async Task RecurrenceRoundTripsWithMonthlyAnchor()
    {
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            new DateOnly(2026, 8, 31));
        TodoItem todoItem = TodoItem.Create(
            Id("recurring"),
            OwnerId,
            "Submit report",
            "Monthly report",
            new DateOnly(2026, 8, 31),
            TodoPriority.High,
            Timestamp,
            recurrence,
            Id("series-1"),
            1);

        await repository.AddAsync(todoItem);
        TodoItem? stored = await repository.GetByIdAsync(todoItem.Id);
        BsonDocument raw = await ReadRawDocumentAsync(todoItem.Id);

        stored.Should().NotBeNull();
        stored!.Recurrence.Should().Be(recurrence);
        stored.SeriesId.Should().Be(Id("series-1"));
        stored.OccurrenceNumber.Should().Be(1);
        raw["recurrence"]["type"].AsString.Should().Be("Monthly");
        raw["recurrence"]["interval"].AsInt32.Should().Be(1);
        raw["recurrence"]["unit"].AsString.Should().Be("Months");
        raw["recurrence"]["anchorDay"].AsInt32.Should().Be(31);
    }

    /// <summary>
    /// Reads project the search tokens away, because the entity recomputes them
    /// and nothing reading a TODO back has a use for them. This guards the risk
    /// that creates: a document loaded without its tokens must not be written
    /// back without them either.
    /// </summary>
    [TestMethod]
    public async Task AReadModifyWriteCycleDoesNotStripSearchTokens()
    {
        TodoItem todoItem = TodoItem.Create(
            Id("round-trip"),
            OwnerId,
            "Submit Quarterly Report",
            "Include the VAT summary",
            new DateOnly(2026, 8, 31),
            TodoPriority.High,
            Timestamp);
        await repository.AddAsync(todoItem);

        // Loaded through the projecting read, changed in a way that leaves the
        // text alone, and written straight back.
        TodoItem loaded = await GetRequiredTodoAsync(todoItem.Id);
        _ = loaded.ChangeStatus(TodoStatus.InProgress, Timestamp.AddHours(1));
        _ = await repository.UpdateAsync(loaded);

        BsonDocument stored = await ReadRawDocumentAsync(todoItem.Id);
        stored["searchTokens"].AsBsonArray.Select(token => token.AsString)
            .Should().Equal(
                "submit",
                "quarterly",
                "report",
                "include",
                "the",
                "vat",
                "summary");
    }

    /// <summary>
    /// The batch read projects tokens away as well, so the same round trip is
    /// measured through it rather than assumed to behave like the single read.
    /// </summary>
    [TestMethod]
    public async Task ABatchReadModifyWriteCycleDoesNotStripSearchTokens()
    {
        TodoItem todoItem = TodoItem.Create(
            Id("batch-round-trip"),
            OwnerId,
            "Renew Passport",
            null,
            new DateOnly(2026, 8, 31),
            TodoPriority.Low,
            Timestamp);
        await repository.AddAsync(todoItem);

        IReadOnlyCollection<TodoItem> loaded = await repository.GetByIdsAsync(
            new[] { todoItem.Id });
        TodoItem batched = loaded.Single();
        _ = batched.ChangeStatus(TodoStatus.InProgress, Timestamp.AddHours(1));
        await repository.SaveBatchAsync(new[] { batched }, Array.Empty<TodoItem>());

        BsonDocument stored = await ReadRawDocumentAsync(todoItem.Id);
        stored["searchTokens"].AsBsonArray.Select(token => token.AsString)
            .Should().Equal("renew", "passport");
    }

    /// <summary>
    /// Search matches the persisted tokens rather than the text, so what a
    /// repository write leaves behind is the contract search depends on.
    /// </summary>
    [TestMethod]
    public async Task WritesPersistSearchTokensForTheNameAndDescription()
    {
        TodoItem todoItem = TodoItem.Create(
            Id("searchable"),
            OwnerId,
            "Submit Quarterly Report",
            "Include the VAT summary",
            new DateOnly(2026, 8, 31),
            TodoPriority.High,
            Timestamp);

        await repository.AddAsync(todoItem);
        BsonDocument added = await ReadRawDocumentAsync(todoItem.Id);

        added["searchTokens"].AsBsonArray.Select(token => token.AsString)
            .Should().Equal(
                "submit",
                "quarterly",
                "report",
                "include",
                "the",
                "vat",
                "summary");

        TodoItem stored = await GetRequiredTodoAsync(todoItem.Id);
        stored.UpdateDetails(
            "Review Invoice",
            null,
            new DateOnly(2026, 9, 1),
            TodoPriority.Low,
            Timestamp.AddHours(1));
        _ = await repository.UpdateAsync(stored);
        BsonDocument updated = await ReadRawDocumentAsync(todoItem.Id);

        updated["searchTokens"].AsBsonArray.Select(token => token.AsString)
            .Should().Equal("review", "invoice");
    }

    /// <summary>
    /// Every identifier is stored as a standard UUID rather than the driver's
    /// legacy C# subtype, so documents stay readable by other drivers and by
    /// queries built outside this codebase.
    /// </summary>
    [TestMethod]
    public async Task IdentifiersAreStoredAsStandardUuids()
    {
        TodoItem dependency = CreateTodo("dependency");
        TodoItem todoItem = CreateRecurringTodo();
        todoItem.AddDependency(dependency.Id, Timestamp);

        await repository.AddAsync(dependency);
        await repository.AddAsync(todoItem);
        BsonDocument raw = await ReadRawDocumentAsync(todoItem.Id);

        AssertStandardUuid(raw["_id"], todoItem.Id);
        AssertStandardUuid(raw["ownerId"], OwnerId);
        AssertStandardUuid(raw["dependencyIds"].AsBsonArray[0], dependency.Id);
        AssertStandardUuid(raw["seriesId"], Id("series-1"));
    }

    /// <summary>
    /// A document written by a newer deployment carries fields this version does
    /// not know. Reading one must not fail, at the top level or inside the
    /// nested recurrence document.
    /// </summary>
    [TestMethod]
    public async Task UnknownStoredFieldsAreIgnoredWhenReading()
    {
        TodoItem todoItem = CreateRecurringTodo();
        await repository.AddAsync(todoItem);

        _ = await database
            .GetCollection<BsonDocument>("todoItems")
            .UpdateOneAsync(
                new BsonDocument(
                    "_id",
                    new BsonBinaryData(todoItem.Id, GuidRepresentation.Standard)),
                new BsonDocument("$set", new BsonDocument
                {
                    { "futureTodoField", "ignored" },
                    { "recurrence.futureRecurrenceField", "ignored" },
                }));
        TodoItem? stored = await repository.GetByIdAsync(todoItem.Id);

        stored.Should().NotBeNull();
        stored!.Id.Should().Be(todoItem.Id);
        stored.Recurrence.Should().Be(todoItem.Recurrence);
        stored.SeriesId.Should().Be(Id("series-1"));
    }

    [TestMethod]
    public async Task GetByIdsLoadsBatchAndHonorsDeletedFilter()
    {
        TodoItem active = CreateTodo("active");
        TodoItem deleted = CreateTodo("deleted");
        deleted.SoftDelete(Timestamp.AddDays(1));
        await repository.AddAsync(active);
        await repository.AddAsync(deleted);

        IReadOnlyCollection<TodoItem> activeOnly = await repository.GetByIdsAsync(
            new[] { active.Id, deleted.Id, Id("missing") });
        IReadOnlyCollection<TodoItem> includingDeleted = await repository.GetByIdsAsync(
            new[] { active.Id, deleted.Id },
            includeDeleted: true);

        activeOnly.Select(todo => todo.Id).Should().Equal(active.Id);
        includingDeleted.Select(todo => todo.Id)
            .Should().BeEquivalentTo(new[] { active.Id, deleted.Id });
    }

    [TestMethod]
    public async Task ActiveDependentPreventsDeletionButArchivedDependentDoesNot()
    {
        TodoItem prerequisite = CreateTodo("prerequisite");
        TodoItem dependent = CreateTodo("dependent");
        dependent.AddDependency(prerequisite.Id, Timestamp.AddHours(1));
        await repository.AddAsync(prerequisite);
        await repository.AddAsync(dependent);

        bool hasActiveDependent = await repository.HasActiveDependentsAsync(
            prerequisite.Id);
        _ = dependent.ChangeStatus(TodoStatus.Archived, Timestamp.AddHours(2));
        _ = await repository.UpdateAsync(dependent);
        bool hasActiveDependentAfterArchive = await repository.HasActiveDependentsAsync(
            prerequisite.Id);

        hasActiveDependent.Should().BeTrue();
        hasActiveDependentAfterArchive.Should().BeFalse();
    }

    [TestMethod]
    public async Task ActiveDependentIdsIgnoreSelectedArchivedAndDeletedDependents()
    {
        TodoItem prerequisite = CreateTodo("prerequisite");
        TodoItem selectedDependent = CreateTodo("dependent-selected");
        TodoItem archivedDependent = CreateTodo("dependent-archived");
        TodoItem deletedDependent = CreateTodo("dependent-deleted");
        TodoItem activeDependent = CreateTodo("dependent-active");
        foreach (TodoItem dependent in new[]
        {
            selectedDependent,
            archivedDependent,
            deletedDependent,
            activeDependent,
        })
        {
            dependent.AddDependency(prerequisite.Id, Timestamp.AddHours(1));
            await repository.AddAsync(dependent);
        }

        await repository.AddAsync(prerequisite);
        _ = archivedDependent.ChangeStatus(TodoStatus.Archived, Timestamp.AddHours(2));
        _ = await repository.UpdateAsync(archivedDependent);
        deletedDependent.SoftDelete(Timestamp.AddHours(2));
        _ = await repository.SoftDeleteAsync(deletedDependent);

        IReadOnlyCollection<Guid> blocking = await repository.GetActiveDependentIdsAsync(
            [prerequisite.Id],
            [prerequisite.Id, selectedDependent.Id]);

        blocking.Should().Equal(activeDependent.Id);
    }

    [TestMethod]
    public async Task ActiveDependentIdsExcludeAnotherOwnersTodo()
    {
        TodoItem prerequisite = CreateTodo("prerequisite");
        TodoItem otherOwnersDependent = CreateTodo("dependent-other", OtherOwnerId);
        otherOwnersDependent.AddDependency(prerequisite.Id, Timestamp.AddHours(1));
        await repository.AddAsync(prerequisite);
        await otherOwnerRepository.AddAsync(otherOwnersDependent);

        IReadOnlyCollection<Guid> blocking = await repository.GetActiveDependentIdsAsync(
            [prerequisite.Id],
            [prerequisite.Id]);

        blocking.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SaveBatchAppliesEveryWriteAndRejectsAStaleMember()
    {
        TodoItem first = CreateTodo("todo-1");
        TodoItem second = CreateTodo("todo-2");
        await repository.AddAsync(first);
        await repository.AddAsync(second);
        TodoItem firstWriter = await GetRequiredTodoAsync(first.Id);
        TodoItem secondWriter = await GetRequiredTodoAsync(second.Id);
        _ = firstWriter.ChangeStatus(TodoStatus.Completed, Timestamp.AddHours(1));
        _ = secondWriter.ChangeStatus(TodoStatus.Completed, Timestamp.AddHours(1));

        await repository.SaveBatchAsync([firstWriter, secondWriter], []);

        (await GetRequiredTodoAsync(first.Id)).Version.Should().Be(2);
        (await GetRequiredTodoAsync(second.Id)).Version.Should().Be(2);

        Func<Task> staleBatch = async () => await repository.SaveBatchAsync(
            [firstWriter],
            []);

        BulkConcurrencyConflictException exception = (await staleBatch.Should()
            .ThrowAsync<BulkConcurrencyConflictException>())
            .Which;
        exception.ResourceIds.Should().Equal(first.Id);
    }

    [TestMethod]
    public async Task SaveBatchRejectsAnotherOwnersTodo()
    {
        TodoItem otherOwnersTodo = CreateTodo("todo-other", OtherOwnerId);
        await otherOwnerRepository.AddAsync(otherOwnersTodo);

        Func<Task> act = async () => await repository.SaveBatchAsync(
            [otherOwnersTodo],
            []);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("A TODO can only be persisted by its owner.");
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
    public async Task SoftDeleteAndRestoreCompleteRecoverableLifecycle()
    {
        TodoItem todoItem = CreateTodo();
        await repository.AddAsync(todoItem);
        TodoItem deleteWriter = await GetRequiredTodoAsync(todoItem.Id);
        DateTimeOffset deletedAt = Timestamp.AddDays(1);
        deleteWriter.SoftDelete(deletedAt);

        TodoItem deletedTodo = await repository.SoftDeleteAsync(deleteWriter);

        deletedTodo.Version.Should().Be(2);
        deletedTodo.DeletedAt.Should().Be(deletedAt);
        deletedTodo.PurgeAt.Should().Be(deletedAt.AddDays(90));
        (await repository.GetByIdAsync(todoItem.Id)).Should().BeNull();
        (await repository.ExistsAsync(todoItem.Id)).Should().BeFalse();
        (await repository.ExistsAsync(todoItem.Id, includeDeleted: true)).Should().BeTrue();
        (await GetDocumentCountAsync(todoItem.Id)).Should().Be(1);

        TodoItem restoreWriter = await GetRequiredTodoAsync(
            todoItem.Id,
            includeDeleted: true);
        DateTimeOffset restoredAt = deletedAt.AddDays(30);
        restoreWriter.Restore(restoredAt);
        TodoItem restoredTodo = await repository.RestoreAsync(restoreWriter);

        restoredTodo.Version.Should().Be(3);
        restoredTodo.DeletedAt.Should().BeNull();
        restoredTodo.PurgeAt.Should().BeNull();
        restoredTodo.UpdatedAt.Should().Be(restoredAt);
        (await repository.ExistsAsync(todoItem.Id)).Should().BeTrue();
        (await GetDocumentCountAsync(todoItem.Id)).Should().Be(1);
    }

    [TestMethod]
    public async Task RepositoryRejectsAggregatesInWrongDeletionState()
    {
        TodoItem activeTodo = CreateTodo();
        TodoItem deletedTodo = CreateTodo();
        deletedTodo.SoftDelete(Timestamp.AddDays(1));

        Func<Task> persistActiveAsDeleted = async () =>
            await repository.SoftDeleteAsync(activeTodo);
        Func<Task> persistDeletedAsRestored = async () =>
            await repository.RestoreAsync(deletedTodo);

        await persistActiveAsDeleted.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("A TODO must be soft-deleted before it can be persisted as deleted.");
        await persistDeletedAsRestored.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("A TODO must be restored before it can be persisted as active.");
    }

    [TestMethod]
    public async Task RepositoryWritesExpectedBsonRepresentations()
    {
        TodoItem todoItem = CreateTodo();
        await repository.AddAsync(todoItem);
        IMongoCollection<BsonDocument> collection = database
            .GetCollection<BsonDocument>("todoItems");

        BsonDocument document = await collection
            .Find(Builders<BsonDocument>.Filter.Eq(
                "_id",
                new BsonBinaryData(todoItem.Id, GuidRepresentation.Standard)))
            .SingleAsync();

        document["_id"].AsBsonBinaryData.SubType.Should()
            .Be(BsonBinarySubType.UuidStandard);
        document["_id"].AsBsonBinaryData.ToGuid().Should().Be(todoItem.Id);
        document["dueDate"].AsString.Should().Be("2026-08-31");
        document["status"].AsInt32.Should().Be((int)TodoStatus.NotStarted);
        document["priority"].AsInt32.Should().Be((int)TodoPriority.High);
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
            TryWriteAsync(repository.UpdateAsync(firstWriter)),
            TryWriteAsync(repository.UpdateAsync(secondWriter)));

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

        Task<TodoItem?> updateTask = TryWriteAsync(
            repository.UpdateAsync(updateWriter));
        Task<TodoItem?> deleteTask = TryWriteAsync(
            repository.SoftDeleteAsync(deleteWriter));
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
            return;
        }

        persistedTodo.DeletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task ConcurrentRestoresWithSameVersionAllowExactlyOneWriter()
    {
        TodoItem todoItem = CreateTodo();
        await repository.AddAsync(todoItem);
        TodoItem deleteWriter = await GetRequiredTodoAsync(todoItem.Id);
        deleteWriter.SoftDelete(Timestamp.AddHours(1));
        _ = await repository.SoftDeleteAsync(deleteWriter);
        TodoItem firstWriter = await GetRequiredTodoAsync(
            todoItem.Id,
            includeDeleted: true);
        TodoItem secondWriter = await GetRequiredTodoAsync(
            todoItem.Id,
            includeDeleted: true);
        firstWriter.Restore(Timestamp.AddHours(2));
        secondWriter.Restore(Timestamp.AddHours(3));

        TodoItem?[] results = await Task.WhenAll(
            TryWriteAsync(repository.RestoreAsync(firstWriter)),
            TryWriteAsync(repository.RestoreAsync(secondWriter)));

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
            todoItem.OwnerId,
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

        Func<Task> act = async () => await repository.UpdateAsync(persistedVersionTwo);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        TodoItem storedTodo = await GetRequiredTodoAsync(todoItem.Id);
        storedTodo.Version.Should().Be(1);
    }

    [TestMethod]
    public async Task ReadsExcludeAnotherOwnersTodo()
    {
        TodoItem otherOwnersTodo = CreateTodo("todo-other", OtherOwnerId);
        await otherOwnerRepository.AddAsync(otherOwnersTodo);

        TodoItem? read = await repository.GetByIdAsync(otherOwnersTodo.Id);
        bool exists = await repository.ExistsAsync(otherOwnersTodo.Id);
        IReadOnlyCollection<TodoItem> batch = await repository.GetByIdsAsync(
            [otherOwnersTodo.Id]);

        read.Should().BeNull();
        exists.Should().BeFalse();
        batch.Should().BeEmpty();
    }

    [TestMethod]
    public async Task MutationsCannotReachAnotherOwnersTodo()
    {
        TodoItem otherOwnersTodo = CreateTodo("todo-other", OtherOwnerId);
        await otherOwnerRepository.AddAsync(otherOwnersTodo);
        otherOwnersTodo.UpdateDetails(
            "Renamed by attacker",
            null,
            otherOwnersTodo.DueDate,
            TodoPriority.Low,
            Timestamp.AddHours(1));

        Func<Task> act = async () => await repository.UpdateAsync(otherOwnersTodo);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        TodoItem? stored = await otherOwnerRepository.GetByIdAsync(
            otherOwnersTodo.Id);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Submit report");
        stored.Version.Should().Be(1);
    }

    [TestMethod]
    public async Task AddRejectsTodoOwnedByAnotherUser()
    {
        TodoItem otherOwnersTodo = CreateTodo("todo-other", OtherOwnerId);

        Func<Task> act = () => repository.AddAsync(otherOwnersTodo);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("A TODO can only be persisted by its owner.");
    }

    [TestMethod]
    public async Task RepositoryRefusesUnauthenticatedReads()
    {
        ITodoRepository anonymous = CreateRepository(Guid.Empty);

        Func<Task> act = () => anonymous.GetByIdAsync(Id("todo-1"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static TodoItem CreateTodo(string id = "todo-1", Guid? ownerId = null)
    {
        return TodoItem.Create(
            Id(id),
            ownerId ?? OwnerId,
            "Submit report",
            "Monthly report",
            new DateOnly(2026, 8, 31),
            TodoPriority.High,
            Timestamp);
    }

    private static TodoItem CreateRecurringTodo(string id = "recurring")
    {
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            new DateOnly(2026, 8, 31));

        return TodoItem.Create(
            Id(id),
            OwnerId,
            "Submit report",
            "Monthly report",
            new DateOnly(2026, 8, 31),
            TodoPriority.High,
            Timestamp,
            recurrence,
            Id("series-1"),
            1);
    }

    private static void AssertStandardUuid(BsonValue value, Guid expected)
    {
        BsonBinaryData binary = value.AsBsonBinaryData;

        binary.SubType.Should().Be(BsonBinarySubType.UuidStandard);
        binary.ToGuid().Should().Be(expected);
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Repository writes now report a lost optimistic-concurrency race by
    /// throwing, so tests that race two writers translate the loser back into a
    /// null result to assert that exactly one of them won.
    /// </summary>
    private static async Task<TodoItem?> TryWriteAsync(Task<TodoItem> write)
    {
        try
        {
            return await write;
        }
        catch (ConcurrencyConflictException)
        {
            return null;
        }
    }

    private static Guid Id(string value)
    {
        byte[] bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }

    /// <summary>
    /// The Mongo implementations are internal to the infrastructure assembly, so
    /// the suite resolves the repository contract from the real registration
    /// rather than constructing the concrete type.
    /// </summary>
    private ITodoRepository CreateRepository(Guid ownerId)
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            [$"{MongoDbSettings.SectionName}:ConnectionString"] = settings.ConnectionString,
            [$"{MongoDbSettings.SectionName}:DatabaseName"] = settings.DatabaseName,
            [$"{MongoDbSettings.SectionName}:TodoItemsCollectionName"] =
                settings.TodoItemsCollectionName,
        };
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new TestCurrentUser(ownerId));
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build());

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        providers.Add(serviceProvider);

        return serviceProvider.GetRequiredService<ITodoRepository>();
    }

    private async Task<BsonDocument> ReadRawDocumentAsync(Guid id)
    {
        return await database
            .GetCollection<BsonDocument>("todoItems")
            .Find(new BsonDocument(
                "_id",
                new BsonBinaryData(id, GuidRepresentation.Standard)))
            .FirstAsync();
    }

    private async Task<TodoItem> GetRequiredTodoAsync(
        Guid id,
        bool includeDeleted = false)
    {
        return await repository.GetByIdAsync(id, includeDeleted)
            ?? throw new InvalidOperationException($"TODO '{id}' should exist for the test.");
    }

    private async Task<long> GetDocumentCountAsync(Guid id)
    {
        IMongoCollection<BsonDocument> collection = database
            .GetCollection<BsonDocument>("todoItems");

        return await collection.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Eq(
                "_id",
                new BsonBinaryData(id, GuidRepresentation.Standard)));
    }
}
