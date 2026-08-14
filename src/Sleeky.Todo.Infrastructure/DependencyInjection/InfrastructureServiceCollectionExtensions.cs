using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Infrastructure.Persistence;
using Sleeky.Todo.Infrastructure.Persistence.Diagnostics;
using Sleeky.Todo.Infrastructure.Persistence.Documents;
using Sleeky.Todo.Infrastructure.Persistence.Health;
using Sleeky.Todo.Infrastructure.Persistence.Indexes;
using Sleeky.Todo.Infrastructure.Persistence.Migrations;
using Sleeky.Todo.Infrastructure.Persistence.Queries;
using Sleeky.Todo.Infrastructure.Persistence.Repositories;
using Sleeky.Todo.Infrastructure.Persistence.Transactions;
using Sleeky.Todo.Infrastructure.Time;

namespace Sleeky.Todo.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<MongoDbSettings>()
            .Bind(configuration.GetSection(MongoDbSettings.SectionName))
            .Validate(
                settings => IsValidConnectionString(settings.ConnectionString),
                $"{MongoDbSettings.SectionName}:ConnectionString must be a valid MongoDB connection string.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.DatabaseName),
                $"{MongoDbSettings.SectionName}:DatabaseName is required.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.TodoItemsCollectionName),
                $"{MongoDbSettings.SectionName}:TodoItemsCollectionName is required.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.UsersCollectionName),
                $"{MongoDbSettings.SectionName}:UsersCollectionName is required.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.AssistantSettingsCollectionName),
                $"{MongoDbSettings.SectionName}:AssistantSettingsCollectionName is required.")
            .ValidateOnStart();

        services.AddSingleton<IMongoClient>(serviceProvider =>
        {
            MongoDbSettings settings = serviceProvider
                .GetRequiredService<IOptions<MongoDbSettings>>()
                .Value;

            // Command timings are diagnostics, so a host that configured no
            // logging still gets a working client rather than a resolve failure.
            ILogger commandLogger = (serviceProvider.GetService<ILoggerFactory>()
                    ?? NullLoggerFactory.Instance)
                .CreateLogger(MongoCommandLogger.LoggerCategory);
            MongoClientSettings clientSettings = MongoClientSettings.FromConnectionString(
                settings.ConnectionString);
            clientSettings.ClusterConfigurator =
                builder => MongoCommandLogger.Configure(builder, commandLogger);

            return new MongoClient(clientSettings);
        });
        services.AddSingleton(serviceProvider =>
        {
            MongoDbSettings settings = serviceProvider
                .GetRequiredService<IOptions<MongoDbSettings>>()
                .Value;
            IMongoClient mongoClient = serviceProvider.GetRequiredService<IMongoClient>();

            return mongoClient.GetDatabase(settings.DatabaseName);
        });
        services.AddSingleton(serviceProvider => ResolveCollection<TodoDocument>(
            serviceProvider,
            settings => settings.TodoItemsCollectionName));
        services.AddSingleton(serviceProvider => ResolveCollection<UserDocument>(
            serviceProvider,
            settings => settings.UsersCollectionName));
        services.AddSingleton(serviceProvider => ResolveCollection<AssistantSettingsDocument>(
            serviceProvider,
            settings => settings.AssistantSettingsCollectionName));
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<MongoTransactionContext>();
        services.AddScoped<ITransactionExecutor, MongoTransactionExecutor>();
        services.AddScoped<ITodoRepository, MongoTodoRepository>();
        services.AddScoped<ITodoListReader, MongoTodoListReader>();
        services.AddScoped<IUserDirectoryRepository, MongoUserDirectoryRepository>();
        services.AddScoped<IAssistantSettingsRepository, MongoAssistantSettingsRepository>();
        services.AddHostedService<MongoDbEnumStorageMigrator>();
        services.AddHostedService<MongoDbIndexInitializer>();
        services
            .AddHealthChecks()
            .AddCheck<MongoDbHealthCheck>("mongodb");

        return services;
    }

    private static IMongoCollection<TDocument> ResolveCollection<TDocument>(
        IServiceProvider serviceProvider,
        Func<MongoDbSettings, string> selectCollectionName)
    {
        MongoDbSettings settings = serviceProvider
            .GetRequiredService<IOptions<MongoDbSettings>>()
            .Value;
        IMongoDatabase database = serviceProvider.GetRequiredService<IMongoDatabase>();

        return database.GetCollection<TDocument>(selectCollectionName(settings));
    }

    private static bool IsValidConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            _ = MongoUrl.Create(connectionString);
            return true;
        }
        catch (MongoConfigurationException)
        {
            return false;
        }
    }
}
