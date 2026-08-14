using System.Security.Claims;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;

using Sleeky.Todo.Application.Abstractions.Identity;

namespace Sleeky.Todo.Api.Authentication;

/// <summary>
/// Translates a validated OpenID Connect login into the application's own
/// principal. The resulting ticket carries only the internal user identifier
/// and display name, so no provider token or raw subject reaches the cookie.
/// </summary>
internal static class OidcAuthenticationEvents
{
    private const string DisplayNameClaim = "name";
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
}
