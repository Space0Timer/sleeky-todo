using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Infrastructure.Persistence;
using Sleeky.Todo.Infrastructure.Persistence.Repositories;

namespace Sleeky.Todo.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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
        services.AddScoped<ITodoRepository, MongoTodoRepository>();

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
