using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.DependencyInjection;
using Sleeky.Todo.Infrastructure.Persistence;
using Sleeky.Todo.Infrastructure.Persistence.Diagnostics;

using Testcontainers.MongoDb;

namespace Sleeky.Todo.IntegrationTests.Persistence;

/// <summary>
/// The accumulator that lets a request's own log entry say what MongoDB cost
/// it, against the real driver.
/// </summary>
/// <remarks>
/// Worth an integration test rather than a unit one because the thing that can
/// break is not the arithmetic. A command's start and its outcome are separate
/// events, and the driver is free to raise the second on a thread that never
/// carried the ambient value — so what is under test is that the two are still
/// matched to each other.
/// </remarks>
[TestClass]
public sealed class DatabaseCommandTallyTests
{
    private static readonly DateTimeOffset Timestamp = new DateTimeOffset(
        2026,
        8,
        12,
        1,
        0,
        0,
        TimeSpan.Zero);

    private static readonly Guid OwnerId =
        Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");

    private static MongoDbContainer? mongoDbContainer;

    private ServiceProvider serviceProvider = null!;
    private ITodoRepository repository = null!;

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
                "Set RUN_MONGODB_INTEGRATION_TESTS=true and start Docker to run MongoDB tally tests.");
        }

        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            [$"{MongoDbSettings.SectionName}:ConnectionString"] =
                mongoDbContainer.GetConnectionString(),
            [$"{MongoDbSettings.SectionName}:DatabaseName"] =
                $"sleekyTodoTallyTests_{Guid.NewGuid():N}",
            [$"{MongoDbSettings.SectionName}:TodoItemsCollectionName"] = "todoItems",
        };

        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new TestCurrentUser(OwnerId));
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build());

        serviceProvider = services.BuildServiceProvider();
        repository = serviceProvider.GetRequiredService<ITodoRepository>();

        // Establishing a connection is itself commands, on a flow that can be
        // the caller's the first time. Paid for before any tally is open, so
        // what a tally sees is the work the test asked for.
        await repository.GetByIdAsync(Guid.NewGuid());
    }

    [TestCleanup]
    public void TestCleanup()
    {
        serviceProvider?.Dispose();
    }

    [TestMethod]
    public async Task CommandsIssuedInsideARequestAreCounted()
    {
        using DatabaseCommandTally tally = DatabaseCommandTally.BeginRequest();

        await repository.AddAsync(CreateTodo());
        await repository.GetByIdAsync(Guid.NewGuid());

        tally.CommandCount.Should().BeGreaterThanOrEqualTo(2);
        tally.TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// Background traffic — heartbeats, the index initializer, anything the
    /// driver does on its own schedule — belongs to no request, and is kept out
    /// by having no ambient tally rather than by matching command names.
    /// </summary>
    [TestMethod]
    public async Task CommandsIssuedOutsideARequestAreNotCounted()
    {
        DatabaseCommandTally tally = DatabaseCommandTally.BeginRequest();
        await repository.GetByIdAsync(Guid.NewGuid());
        int countedWhileOpen = tally.CommandCount;
        tally.Dispose();

        await repository.AddAsync(CreateTodo());
        await repository.GetByIdAsync(Guid.NewGuid());

        countedWhileOpen.Should().BeGreaterThan(0);
        tally.CommandCount.Should().Be(countedWhileOpen);
    }

    private static bool ShouldRunMongoDbTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_MONGODB_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static TodoItem CreateTodo()
    {
        return TodoItem.Create(
            Guid.NewGuid(),
            OwnerId,
            "Tally a command",
            "Runs one write so the tally has something to total",
            new DateOnly(2026, 8, 20),
            TodoPriority.Medium,
            Timestamp,
            null,
            null);
    }
}
