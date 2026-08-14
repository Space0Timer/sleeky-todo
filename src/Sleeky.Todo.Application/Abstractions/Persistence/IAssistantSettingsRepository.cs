namespace Sleeky.Todo.Application.Abstractions.Persistence;

public interface IAssistantSettingsRepository
{
    Task<AssistantSettingsRecord?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        AssistantSettingsRecord settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a user's configuration.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when there was nothing stored, so a caller can
    /// answer 404 rather than reporting a deletion that did not happen.
    /// </returns>
    Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
