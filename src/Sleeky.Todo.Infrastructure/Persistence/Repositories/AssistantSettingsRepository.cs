using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Repositories;

internal sealed class AssistantSettingsRepository : IAssistantSettingsRepository
{
    private readonly IMongoCollection<AssistantSettingsDocument> settings;
    private readonly IClock clock;

    public AssistantSettingsRepository(
        IMongoCollection<AssistantSettingsDocument> settings,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);

        this.settings = settings;
        this.clock = clock;
    }

    public async Task<AssistantSettingsRecord?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        AssistantSettingsDocument? document = await settings
            .Find(Builders<AssistantSettingsDocument>.Filter.Eq(entry => entry.UserId, userId))
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : ToRecord(document);
    }

    public async Task SaveAsync(
        AssistantSettingsRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        UpdateDefinitionBuilder<AssistantSettingsDocument> updates =
            Builders<AssistantSettingsDocument>.Update;
        UpdateDefinition<AssistantSettingsDocument> update = updates.Combine(
            updates.Set(entry => entry.Provider, record.Provider),
            updates.Set(entry => entry.BaseUrl, record.BaseUrl),
            updates.Set(entry => entry.Model, record.Model),
            updates.Set(entry => entry.ProtectedApiKey, record.ProtectedApiKey),
            updates.Set(entry => entry.UpdatedAt, clock.UtcNow.UtcDateTime));

        await settings.UpdateOneAsync(
            Builders<AssistantSettingsDocument>.Filter.Eq(
                entry => entry.UserId,
                record.UserId),
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        DeleteResult result = await settings.DeleteOneAsync(
            Builders<AssistantSettingsDocument>.Filter.Eq(entry => entry.UserId, userId),
            cancellationToken);

        return result.DeletedCount > 0;
    }

    private static AssistantSettingsRecord ToRecord(AssistantSettingsDocument document)
    {
        return new AssistantSettingsRecord(
            document.UserId,
            document.Provider,
            document.BaseUrl,
            document.Model,
            document.ProtectedApiKey);
    }
}
