namespace Sleeky.Todo.Api.Authentication;

public sealed class AuthenticationSettings
{
    public const string SectionName = "Authentication";

    public string Authority { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string CallbackPath { get; init; } = "/signin-oidc";

    public string SignedOutCallbackPath { get; init; } = "/signout-callback-oidc";

    /// <summary>
    /// Gets a value indicating whether provider metadata must be retrieved over
    /// HTTPS. Only a local development provider may set this to false.
    /// </summary>
    public bool RequireHttpsMetadata { get; init; } = true;

    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(8);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority);
}
