namespace Sleeky.Todo.Api.Hosting;

/// <summary>
/// The response headers that bound what a browser will do with what this host
/// serves.
/// </summary>
/// <remarks>
/// The client escapes everything it renders, so none of this is load-bearing
/// today. It is here for the change that makes it so: the assistant panel
/// displays text written by a model, and the ordinary way to improve that panel
/// is a Markdown renderer — one <c>dangerouslySetInnerHTML</c>, at which point
/// every stored TODO name and description becomes a script the browser is
/// willing to run. A policy already in place costs nothing and is what stands
/// between that change and an account-wide compromise.
///
/// Written as one middleware rather than per-endpoint so a route added later is
/// covered without anyone remembering to cover it.
/// </remarks>
public static class SecurityHeaders
{
    /// <summary>
    /// Everything from this origin and nothing from anywhere else.
    /// </summary>
    /// <remarks>
    /// The client loads no third-party script, style, font, or image, and talks
    /// to no origin but its own, so the strict policy is simply what it already
    /// does. <c>data:</c> is allowed for images because the build inlines small
    /// assets that way.
    ///
    /// No <c>unsafe-inline</c> anywhere: the shell carries one module script tag
    /// and no inline script, and the build is configured to keep it that way by
    /// leaving out the module-preload polyfill, which would otherwise be
    /// inlined. <c>frame-ancestors</c> replaces X-Frame-Options, which is
    /// obsolete but still sent for anything that predates the directive.
    /// </remarks>
    private const string PolicyWithoutFormAction =
        "default-src 'self'; "
        + "script-src 'self'; "
        + "style-src 'self'; "
        + "img-src 'self' data:; "
        + "font-src 'self'; "
        + "connect-src 'self'; "
        + "frame-ancestors 'none'; "
        + "base-uri 'self'; "
        + "object-src 'none'; ";

    /// <summary>
    /// Builds the policy for a deployment. <paramref name="providerAuthority"/>
    /// is the configured OpenID Connect authority, whose origin has to appear in
    /// <c>form-action</c>: sign-out is a form post that redirects to the
    /// provider's end-session endpoint, and browsers re-check the directive
    /// against each redirect the submission follows rather than only against
    /// where the form was aimed. Under <c>'self'</c> alone, sign-out would be
    /// blocked at the redirect and the session would survive at the provider.
    /// </summary>
    public static string BuildContentSecurityPolicy(string? providerAuthority)
    {
        string formAction = Uri.TryCreate(
            providerAuthority,
            UriKind.Absolute,
            out Uri? authority)
            ? $"'self' {authority.GetLeftPart(UriPartial.Authority)}"
            : "'self'";

        return $"{PolicyWithoutFormAction}form-action {formAction}";
    }

    public static IApplicationBuilder UseSecurityHeaders(
        this IApplicationBuilder app,
        string? providerAuthority)
    {
        ArgumentNullException.ThrowIfNull(app);

        string contentSecurityPolicy = BuildContentSecurityPolicy(providerAuthority);

        return app.Use(async (context, next) =>
        {
            IHeaderDictionary headers = context.Response.Headers;

            headers.ContentSecurityPolicy = contentSecurityPolicy;

            // The API answers JSON and the host serves hashed assets, so there
            // is no response here whose type a browser should be guessing at.
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";

            // A TODO name reaches the URL of no request that leaves this origin,
            // but the referrer is sent on every outbound navigation, and the
            // client has no need of it.
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";

            await next();
        });
    }
}
