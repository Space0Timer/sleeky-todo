namespace Sleeky.Todo.Assistant.Providers;

public interface IAssistantSettingsService
{
    /// <summary>
    /// Reports the configuration in effect, without the key.
    /// </summary>
    Task<AssistantSettingsView> DescribeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the connection a turn should run on.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when nothing usable is configured, which the
    /// caller reports as "set up a provider" rather than as a failure.
    /// </returns>
    Task<AssistantConnection?> ResolveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a connection from settings that have not been saved, so a
    /// probe can answer for what is on the form rather than for what is stored.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the draft names no usable key, counting the
    /// stored one: a user editing a model has no way to retype a key they
    /// cannot read.
    /// </returns>
    Task<AssistantConnection?> ResolveDraftAsync(
        AssistantSettingsInput input,
        CancellationToken cancellationToken = default);

    Task SaveAsync(AssistantSettingsInput input, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(CancellationToken cancellationToken = default);
}
