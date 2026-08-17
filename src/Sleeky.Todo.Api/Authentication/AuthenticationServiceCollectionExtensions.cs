using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;

using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using Sleeky.Todo.Application.Abstractions.Identity;

namespace Sleeky.Todo.Api.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";

    private const string DevelopmentCookieName = "sleeky-session";
    private const string SecureCookieName = "__Host-sleeky-session";

    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        AuthenticationSettings settings = configuration
            .GetSection(AuthenticationSettings.SectionName)
            .Get<AuthenticationSettings>()
            ?? new AuthenticationSettings();

        // The __Host- prefix requires the Secure attribute, which a plain-HTTP
        // development origin cannot satisfy.
        bool requireSecureCookie = !environment.IsDevelopment();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.SecurePolicy = requireSecureCookie
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        });

        AuthenticationBuilder authentication = services
            .AddAuthentication(options =>
            {
                // The cookie scheme must also be the challenge scheme. With the
                // OpenID Connect scheme here, an unauthenticated API request is
                // answered with a provider redirect the client cannot follow
                // instead of the 401 it can act on.
                options.DefaultScheme = AuthenticationSchemes.ApplicationCookie;
                options.DefaultChallengeScheme = AuthenticationSchemes.ApplicationCookie;
            })
            .AddCookie(
                AuthenticationSchemes.ApplicationCookie,
                options => ConfigureCookie(options, settings, requireSecureCookie));

        if (settings.IsConfigured)
        {
            authentication.AddOpenIdConnect(
                AuthenticationSchemes.Oidc,
                options => ConfigureOpenIdConnect(options, settings));
        }

        // Endpoints without their own authorization metadata require an
        // authenticated user, so leaving one open is a deliberate
        // [AllowAnonymous] rather than a forgotten [Authorize].
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    private static void ConfigureCookie(
        CookieAuthenticationOptions options,
        AuthenticationSettings settings,
        bool requireSecureCookie)
    {
        options.Cookie.Name = requireSecureCookie
            ? SecureCookieName
            : DevelopmentCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = requireSecureCookie
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = settings.SessionLifetime;
        options.SlidingExpiration = true;

        // This host only serves the API and the OpenID Connect callback, so an
        // authentication failure is always a status code rather than a redirect.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    private static void ConfigureOpenIdConnect(
        OpenIdConnectOptions options,
        AuthenticationSettings settings)
    {
        options.Authority = settings.Authority;
        options.ClientId = settings.ClientId;
        options.ClientSecret = settings.ClientSecret;
        options.CallbackPath = settings.CallbackPath;
        options.SignedOutCallbackPath = settings.SignedOutCallbackPath;
        options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = false;
        options.MapInboundClaims = false;

        // The token-validated event reads the subject, issuer, and display name
        // from the ID token and replaces the principal with the application's
        // own. The userinfo response would arrive afterwards and re-apply the
        // default claim actions, putting the provider subject and profile
        // claims back into a ticket that deliberately excludes them.
        options.GetClaimsFromUserInfoEndpoint = false;
        options.SignInScheme = AuthenticationSchemes.ApplicationCookie;
        options.Scope.Clear();
        options.Scope.Add(OpenIdConnectScope.OpenId);
        options.Scope.Add(OpenIdConnectScope.Profile);

        // Asked for so the ID token carries an address the user directory can
        // record. It is what makes someone findable by e-mail when a colleague
        // shares a Space with them, and it never reaches the principal.
        options.Scope.Add(OpenIdConnectScope.Email);
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = OidcAuthenticationEvents.OnTokenValidatedAsync,
            OnAuthenticationFailed = OidcAuthenticationEvents.OnAuthenticationFailedAsync,
        };
    }
}
