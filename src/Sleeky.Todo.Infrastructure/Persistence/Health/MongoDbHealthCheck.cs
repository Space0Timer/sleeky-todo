using Microsoft.Extensions.Diagnostics.HealthChecks;

using MongoDB.Bson;
using MongoDB.Driver;

namespace Sleeky.Todo.Infrastructure.Persistence.Health;

internal sealed class MongoDbHealthCheck : IHealthCheck
{
    private readonly IMongoDatabase database;

    public MongoDbHealthCheck(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        this.database = database;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy("MongoDB is reachable.");
        }
        catch (MongoException exception)
        {
            return HealthCheckResult.Unhealthy(
                "MongoDB is not reachable.",
                exception);
        }
    }
}
