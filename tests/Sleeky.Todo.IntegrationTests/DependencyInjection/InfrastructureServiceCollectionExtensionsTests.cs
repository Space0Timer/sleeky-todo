using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Infrastructure.DependencyInjection;
using Sleeky.Todo.Infrastructure.Persistence;
using Sleeky.Todo.Infrastructure.Persistence.Repositories;

namespace Sleeky.Todo.IntegrationTests.DependencyInjection;

[TestClass]
public sealed class InfrastructureServiceCollectionExtensionsTests
{
    [TestMethod]
    public void AddInfrastructureBindsSettingsAndRegistersMongoServices()
    {
        IConfiguration configuration = CreateConfiguration(
            "mongodb://localhost:27017/?replicaSet=rs0",
            "sleekyTodo",
            "todoItems");
        ServiceCollection services = new ServiceCollection();

        // The current user is supplied by the composition root, because only
        // the API layer can read it from the request.
        services.AddSingleton<ICurrentUser>(
            new TestCurrentUser(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        services.AddInfrastructure(configuration);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        MongoDbSettings settings = serviceProvider
            .GetRequiredService<IOptions<MongoDbSettings>>()
            .Value;
        IMongoClient mongoClient = serviceProvider.GetRequiredService<IMongoClient>();
        IMongoDatabase database = serviceProvider.GetRequiredService<IMongoDatabase>();
        IClock clock = serviceProvider.GetRequiredService<IClock>();
        ITodoRepository repository = serviceProvider.GetRequiredService<ITodoRepository>();
        ITransactionExecutor transactionExecutor = serviceProvider
            .GetRequiredService<ITransactionExecutor>();
        IUserDirectoryRepository userDirectoryRepository = serviceProvider
            .GetRequiredService<IUserDirectoryRepository>();

        settings.DatabaseName.Should().Be("sleekyTodo");
        settings.TodoItemsCollectionName.Should().Be("todoItems");
        settings.UsersCollectionName.Should().Be("users");
        mongoClient.Should().BeOfType<MongoClient>();
        database.DatabaseNamespace.DatabaseName.Should().Be("sleekyTodo");
        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
        repository.Should().BeOfType<MongoTodoRepository>();
        transactionExecutor.Should().NotBeNull();
        userDirectoryRepository.Should().BeOfType<MongoUserDirectoryRepository>();
    }

    [TestMethod]
    public void AddInfrastructureRejectsInvalidSettings()
    {
        IConfiguration configuration = CreateConfiguration(
            "not-a-mongodb-connection-string",
            string.Empty,
            string.Empty);
        ServiceCollection services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        Action act = () => _ = serviceProvider
            .GetRequiredService<IOptions<MongoDbSettings>>()
            .Value;

        OptionsValidationException exception = act.Should()
            .Throw<OptionsValidationException>()
            .Which;
        exception.Failures.Should().HaveCount(3);
    }

    private static IConfiguration CreateConfiguration(
        string connectionString,
        string databaseName,
        string collectionName)
    {
        Dictionary<string, string?> values = new Dictionary<string, string?>
        {
            [$"{MongoDbSettings.SectionName}:ConnectionString"] = connectionString,
            [$"{MongoDbSettings.SectionName}:DatabaseName"] = databaseName,
            [$"{MongoDbSettings.SectionName}:TodoItemsCollectionName"] = collectionName,
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
