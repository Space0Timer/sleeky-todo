using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Sleeky.Todo.Api.Contracts.Assistant;
using Sleeky.Todo.Assistant.Providers;

namespace Sleeky.Todo.Api.Controllers;

/// <summary>
/// The user's own provider configuration.
/// </summary>
/// <remarks>
/// Write-only where the key is concerned. Nothing here returns it, and there is
/// no route that could: a key can be replaced but never retrieved, so a stolen
/// session cannot be used to walk away with the user's credentials.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/assistant/settings")]
public sealed class AssistantSettingsController : ControllerBase
{
    private const string MalformedBaseUrl =
        "The base URL must be an absolute http or https address, such as "
        + "http://localhost:11434/v1.";

    private readonly IAssistantSettingsService settings;

    private readonly IAssistantConnectionProbe probe;

    public AssistantSettingsController(
        IAssistantSettingsService settings,
        IAssistantConnectionProbe probe)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(probe);

        this.settings = settings;
        this.probe = probe;
    }

    [HttpGet]
    [ProducesResponseType<AssistantSettingsView>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AssistantSettingsView>> Get(
        CancellationToken cancellationToken)
    {
        return Ok(await settings.DescribeAsync(cancellationToken));
    }

    [HttpPut]
    [ProducesResponseType<AssistantSettingsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AssistantSettingsView>> Save(
        SaveAssistantSettingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            ModelState.AddModelError(nameof(request.Model), "A model is required.");
        }

        if (!Enum.TryParse(request.Provider, out AssistantProvider provider)
            || !Enum.IsDefined(provider))
        {
            ModelState.AddModelError(
                nameof(request.Provider),
                $"Provider must be one of: {string.Join(", ", Enum.GetNames<AssistantProvider>())}.");
        }

        // Caught here rather than left to resolution, so a mistyped endpoint is
        // a field error on the form instead of a key quietly sent to whichever
        // host the provider defaults to.
        if (!AssistantBaseUrl.TryParse(request.BaseUrl, out Uri? _))
        {
            ModelState.AddModelError(nameof(request.BaseUrl), MalformedBaseUrl);
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        await settings.SaveAsync(
            new AssistantSettingsInput(
                provider,
                request.BaseUrl,
                request.Model,
                request.ApiKey),
            cancellationToken);

        return Ok(await settings.DescribeAsync(cancellationToken));
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(CancellationToken cancellationToken)
    {
        bool removed = await settings.DeleteAsync(cancellationToken);

        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Checks a configuration without saving it, so a wrong key or an unknown
    /// model is caught while the user is still on the form.
    /// </summary>
    /// <remarks>
    /// The body is optional and carries the values on the form. Probing the
    /// stored settings instead would answer for a key the user has just
    /// replaced, which is the opposite of what the button is for. Nothing sent
    /// here is persisted, and the probe strips the key from any message it
    /// reports.
    /// </remarks>
    [HttpPost("test")]
    [ProducesResponseType<AssistantProbeResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AssistantProbeResult>> Test(
        [FromBody] SaveAssistantSettingsRequest? request,
        CancellationToken cancellationToken)
    {
        AssistantConnection? connection = await ResolveProbeTargetAsync(
            request,
            cancellationToken);

        if (connection is null)
        {
            return Ok(new AssistantProbeResult(
                Succeeded: false,
                "No provider is configured. Add a provider, model, and API key first."));
        }

        return Ok(await probe.ProbeAsync(connection, cancellationToken));
    }

    private async Task<AssistantConnection?> ResolveProbeTargetAsync(
        SaveAssistantSettingsRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Model))
        {
            return await settings.ResolveAsync(cancellationToken);
        }

        if (!Enum.TryParse(request.Provider, out AssistantProvider provider)
            || !Enum.IsDefined(provider))
        {
            return null;
        }

        return await settings.ResolveDraftAsync(
            new AssistantSettingsInput(
                provider,
                request.BaseUrl,
                request.Model,
                request.ApiKey),
            cancellationToken);
    }
}
