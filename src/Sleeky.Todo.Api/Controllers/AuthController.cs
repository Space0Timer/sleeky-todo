using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using Sleeky.Todo.Api.Authentication;
using Sleeky.Todo.Api.Contracts.Auth;
using Sleeky.Todo.Application.Abstractions.Identity;

namespace Sleeky.Todo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string DefaultReturnUrl = "/";

    private readonly IAntiforgery antiforgery;
    private readonly AntiforgeryOptions antiforgeryOptions;
    private readonly ICurrentUser currentUser;
    private readonly ILogger<AuthController> logger;

    public AuthController(
        IAntiforgery antiforgery,
        IOptions<AntiforgeryOptions> antiforgeryOptions,
        ICurrentUser currentUser,
        ILogger<AuthController> logger)
    {
        ArgumentNullException.ThrowIfNull(antiforgery);
        ArgumentNullException.ThrowIfNull(antiforgeryOptions);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(logger);

        this.antiforgery = antiforgery;
        this.antiforgeryOptions = antiforgeryOptions.Value;
        this.currentUser = currentUser;
        this.logger = logger;
    }

    [HttpGet("login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        if (!TryResolveReturnUrl(returnUrl, out string safeReturnUrl))
        {
            return Problem(
                title: "Invalid return URL.",
                detail: "The login return URL must be a local path.",
                statusCode: StatusCodes.Status400BadRequest,
                instance: Request.Path);
        }

        AuthenticationProperties properties = new AuthenticationProperties
        {
            RedirectUri = safeReturnUrl,
        };

        return Challenge(properties, AuthenticationSchemes.Oidc);
    }

    [HttpGet("me")]
    [AllowAnonymous]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    public ActionResult<CurrentUserResponse> Me()
    {
        if (!currentUser.IsAuthenticated)
        {
            return Ok(new CurrentUserResponse(false, null, null));
        }

        return Ok(new CurrentUserResponse(
            true,
            currentUser.UserId,
            currentUser.DisplayName));
    }

    [HttpGet("antiforgery")]
    [AllowAnonymous]
    [ProducesResponseType<AntiforgeryTokenResponse>(StatusCodes.Status200OK)]
    public ActionResult<AntiforgeryTokenResponse> Antiforgery()
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new AntiforgeryTokenResponse(
            tokens.RequestToken ?? string.Empty,
            antiforgeryOptions.HeaderName ?? string.Empty));
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        Guid userId = currentUser.UserId;

        await HttpContext.SignOutAsync(AuthenticationSchemes.ApplicationCookie);

        this.logger.LogInformation(1203, "Signed out user {UserId}", userId);

        return NoContent();
    }

    private bool TryResolveReturnUrl(string? returnUrl, out string safeReturnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            safeReturnUrl = DefaultReturnUrl;
            return true;
        }

        safeReturnUrl = returnUrl;

        return Url.IsLocalUrl(returnUrl);
    }
}
