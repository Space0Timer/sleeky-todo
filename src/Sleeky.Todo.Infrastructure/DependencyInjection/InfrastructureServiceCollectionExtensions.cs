using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Infrastructure.Persistence;
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
            .ValidateOnStart();

        services.AddSingleton<IMongoClient>(serviceProvider =>
        {
            MongoDbSettings settings = serviceProvider
                .GetRequiredService<IOptions<MongoDbSettings>>()
                .Value;

            return new MongoClient(settings.ConnectionString);
        });
        services.AddSingleton(serviceProvider =>
        {
            MongoDbSettings settings = serviceProvider
                .GetRequiredService<IOptions<MongoDbSettings>>()
                .Value;
            IMongoClient mongoClient = serviceProvider.GetRequiredService<IMongoClient>();

            return mongoClient.GetDatabase(settings.DatabaseName);
        });
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<MongoTransactionContext>();
        services.AddScoped<ITodoTransaction, MongoTodoTransaction>();
        services.AddScoped<ITodoRepository, MongoTodoRepository>();
        services.AddScoped<ITodoListReader, MongoTodoListReader>();
        services.AddHostedService<MongoDbEnumStorageMigrator>();
        services.AddHostedService<MongoDbIndexInitializer>();
        services
            .AddHealthChecks()
            .AddCheck<MongoDbHealthCheck>("mongodb");

        return services;
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
