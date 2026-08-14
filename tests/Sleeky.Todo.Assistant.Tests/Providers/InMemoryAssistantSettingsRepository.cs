using Sleeky.Todo.Application.Abstractions.Persistence;

namespace Sleeky.Todo.Assistant.Tests.Providers;

internal sealed class InMemoryAssistantSettingsRepository : IAssistantSettingsRepository
{
    private readonly Dictionary<Guid, AssistantSettingsRecord> stored =
        new Dictionary<Guid, AssistantSettingsRecord>();

    public IReadOnlyCollection<AssistantSettingsRecord> Saved => this.stored.Values;

    public Task<AssistantSettingsRecord?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            this.stored.TryGetValue(userId, out AssistantSettingsRecord? record) ? record : null);
    }

    public Task SaveAsync(
        AssistantSettingsRecord settings,
        CancellationToken cancellationToken = default)
    {
        this.stored[settings.UserId] = settings;

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(this.stored.Remove(userId));
    }
}
