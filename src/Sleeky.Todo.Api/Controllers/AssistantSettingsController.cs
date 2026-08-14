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
            return ValidationProblem(ModelState);
        }

        await settings.SaveAsync(
            new AssistantSettingsInput(
                request.Provider,
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
    /// Checks the configuration in effect without saving anything, so a wrong
    /// key or an unknown model is caught while the user is still on the form.
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType<AssistantProbeResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AssistantProbeResult>> Test(
        CancellationToken cancellationToken)
    {
        AssistantConnection? connection = await settings.ResolveAsync(cancellationToken);

        if (connection is null)
        {
            return Ok(new AssistantProbeResult(
                Succeeded: false,
                "No provider is configured. Add a provider, model, and API key first."));
        }

        return Ok(await probe.ProbeAsync(connection, cancellationToken));
    }
}
