using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sleeky.Todo.Api.Authentication;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// Authenticates a request from a test-only header so a suite can act as a
/// chosen user. This handler exists solely in the test host; the API never
/// registers it, so there is no production bypass.
/// </summary>
internal sealed class TestAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Testing";
    public const string UserIdHeaderName = "X-Test-User-Id";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string headerValue = Request.Headers[UserIdHeaderName].ToString();

        if (!Guid.TryParse(headerValue, out Guid userId) || userId == Guid.Empty)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        ClaimsIdentity identity = new ClaimsIdentity(
            [
                new Claim(TodoClaimTypes.UserId, userId.ToString()),
                new Claim(TodoClaimTypes.DisplayName, $"Test user {userId:N}"),
            ],
            SchemeName);
        AuthenticationTicket ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
