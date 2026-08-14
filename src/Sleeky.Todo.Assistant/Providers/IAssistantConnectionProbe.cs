namespace Sleeky.Todo.Assistant.Providers;

/// <summary>
/// Checks that a provider, model, and key actually work together, so a mistake
/// is caught while the user is still looking at the form rather than in the
/// middle of their first request.
/// </summary>
public interface IAssistantConnectionProbe
{
    Task<AssistantProbeResult> ProbeAsync(
        AssistantConnection connection,
        CancellationToken cancellationToken = default);
}
