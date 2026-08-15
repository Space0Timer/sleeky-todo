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

    /// <summary>
    /// Where the browser lands once sign-out has been through the provider.
    /// The client's own route guard would send an unauthenticated visitor from
    /// the application root to the same place, so naming it here only saves a
    /// redirect and the flash of an application shell nobody stays on.
    /// </summary>
    private const string LoginPath = "/login";

    private readonly IAntiforgery antiforgery;
    private readonly AntiforgeryOptions antiforgeryOptions;
    private readonly ICurrentUser currentUser;
    private readonly ILogger<AuthController> logger;
    private readonly IAuthenticationSchemeProvider schemeProvider;

    public AuthController(
        IAntiforgery antiforgery,
        IOptions<AntiforgeryOptions> antiforgeryOptions,
        ICurrentUser currentUser,
        ILogger<AuthController> logger,
        IAuthenticationSchemeProvider schemeProvider)
    {
        ArgumentNullException.ThrowIfNull(antiforgery);
        ArgumentNullException.ThrowIfNull(antiforgeryOptions);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(schemeProvider);

        this.antiforgery = antiforgery;
        this.antiforgeryOptions = antiforgeryOptions.Value;
        this.currentUser = currentUser;
        this.logger = logger;
        this.schemeProvider = schemeProvider;
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
            antiforgeryOptions.HeaderName ?? string.Empty,
            tokens.FormFieldName));
    }

    /// <summary>
    /// Ends the application session and the provider session with it. The
    /// response is a redirect rather than a status code, so the client submits
    /// this as a form navigation: reaching the provider's end-session endpoint
    /// means handing the browser a redirect, which a <c>fetch</c> cannot follow.
    /// The antiforgery token travels in the form field, so the global
    /// validation filter still covers the request.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        Guid userId = currentUser.UserId;

        this.logger.LogInformation(1203, "Signed out user {UserId}", userId);

        // A deployment without a configured Authority never registers the
        // OpenID Connect scheme, and signing out of a scheme that has no
        // handler throws. Asking the scheme provider keeps this in step with
        // what was actually registered instead of re-deriving it from
        // configuration.
        AuthenticationScheme? providerScheme = await schemeProvider
            .GetSchemeAsync(AuthenticationSchemes.Oidc);

        if (providerScheme is null)
        {
            await HttpContext.SignOutAsync(AuthenticationSchemes.ApplicationCookie);

            return LocalRedirect(LoginPath);
        }

        // The cookie scheme deletes the session, then the OpenID Connect scheme
        // redirects to the provider's end-session endpoint. RedirectUri is
        // where the browser lands after the provider returns it to
        // SignedOutCallbackPath, not the address given to the provider.
        AuthenticationProperties properties = new AuthenticationProperties
        {
            RedirectUri = LoginPath,
        };

        return SignOut(
            properties,
            AuthenticationSchemes.ApplicationCookie,
            AuthenticationSchemes.Oidc);
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
