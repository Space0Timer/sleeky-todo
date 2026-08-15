using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;

namespace Sleeky.Todo.Api.Authentication;

/// <summary>
/// Translates a validated OpenID Connect login into the application's own
/// principal. The resulting ticket carries the internal user identifier, a
/// display name, and the ID token that provider sign-out needs as its
/// <c>id_token_hint</c>. No access or refresh token is persisted, and the
/// ticket is encrypted inside an HttpOnly cookie, so nothing here is reachable
/// from script.
/// </summary>
internal static class OidcAuthenticationEvents
{
    private const string DisplayNameClaim = "name";
    private const string IdTokenName = "id_token";
    private const string IssuerClaim = "iss";
    private const string PreferredUsernameClaim = "preferred_username";
    private const string SubjectClaim = "sub";

    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ClaimsPrincipal? principal = context.Principal;
        string? subject = principal?.FindFirstValue(SubjectClaim);

        if (principal is null || string.IsNullOrWhiteSpace(subject))
        {
            context.Fail("The identity provider returned no subject claim.");
            return;
        }

        string issuer = principal.FindFirstValue(IssuerClaim)
            ?? context.Options.Authority
            ?? string.Empty;
        string? displayName = principal.FindFirstValue(DisplayNameClaim)
            ?? principal.FindFirstValue(PreferredUsernameClaim);

        IUserDirectoryRepository userDirectoryRepository = context.HttpContext.RequestServices
            .GetRequiredService<IUserDirectoryRepository>();
        UserIdentity identity = await userDirectoryRepository.ResolveAsync(
            issuer,
            subject,
            displayName,
            context.HttpContext.RequestAborted);

        context.Principal = BuildApplicationPrincipal(identity, context.Scheme.Name);

        StoreIdTokenForSignOut(context);

        CreateLogger(context.HttpContext).LogInformation(
            1200,
            "Signed in user {UserId}",
            identity.UserId);
    }

    public static Task OnAuthenticationFailedAsync(
        AuthenticationFailedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        CreateLogger(context.HttpContext).LogWarning(
            1201,
            "Interactive login failed: {FailureType} {FailureMessage}",
            context.Exception.GetType().Name,
            context.Exception.Message);

        return Task.CompletedTask;
    }

    private static ClaimsPrincipal BuildApplicationPrincipal(
        UserIdentity identity,
        string authenticationType)
    {
        List<Claim> claims =
        [
            new Claim(TodoClaimTypes.UserId, identity.UserId.ToString()),
        ];

        if (!string.IsNullOrWhiteSpace(identity.DisplayName))
        {
            claims.Add(new Claim(TodoClaimTypes.DisplayName, identity.DisplayName));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

    private static ILogger CreateLogger(HttpContext httpContext)
    {
        return httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(OidcAuthenticationEvents).FullName!);
    }

    /// <summary>
    /// Keeps the ID token in the application ticket so provider sign-out can
    /// present it as <c>id_token_hint</c>. The sign-out handler reads it back
    /// through <c>GetTokenAsync</c> against its sign-in scheme, which is this
    /// application's cookie. Storing the one token here rather than enabling
    /// <c>SaveTokens</c> keeps the access and refresh tokens out of the ticket:
    /// the application calls no provider API, so persisting them would grow the
    /// cookie and widen what a stolen session yields for no gain.
    /// </summary>
    private static void StoreIdTokenForSignOut(TokenValidatedContext context)
    {
        // Under the authorization code flow the ID token arrives in the token
        // endpoint response. The authorization response carries one only in the
        // hybrid flow, which this client does not use, so it is a fallback
        // rather than the expected source.
        string? idToken = context.TokenEndpointResponse?.IdToken
            ?? context.ProtocolMessage?.IdToken;

        if (string.IsNullOrEmpty(idToken) || context.Properties is null)
        {
            // Sign-out still works without the hint; the provider asks the user
            // to confirm rather than ending the session straight away.
            return;
        }

        context.Properties.StoreTokens(
            [new AuthenticationToken { Name = IdTokenName, Value = idToken }]);
    }
}
