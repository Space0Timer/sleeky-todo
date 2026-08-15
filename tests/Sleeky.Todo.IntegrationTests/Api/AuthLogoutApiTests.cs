using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Sleeky.Todo.Api.Contracts.Auth;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// Covers sign-out, which the client submits as a form post so the browser can
/// follow the redirect to the provider. Nothing here needs a database or a
/// provider: what is under test is that the endpoint accepts a token from the
/// form field, still refuses a request without one, and answers with a redirect
/// rather than a status code the browser would sit on.
/// </summary>
[TestClass]
public sealed class AuthLogoutApiTests
{
    private static readonly Guid UserId =
        Guid.Parse("33333333-3333-4333-8333-333333333333");

    private ApiStartupFactory factory = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        factory = new ApiStartupFactory(
            configureServices: services => services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { }));
    }

    [TestCleanup]
    public void TestCleanup()
    {
        factory?.Dispose();
    }

    /// <summary>
    /// The path a browser takes. The token travels in the form field rather
    /// than a header, because a form post is the one way a browser-owned
    /// navigation can carry it, and validation reads the field ahead of the
    /// header for form content types. A regression here answers the sign-out
    /// navigation with a bare 400 page.
    /// </summary>
    [TestMethod]
    public async Task LogoutAcceptsItsTokenFromTheFormField()
    {
        using HttpClient client = CreateClient();
        AntiforgeryTokenResponse token = await RequestTokenAsync(client);

        using FormUrlEncodedContent form = new FormUrlEncodedContent(
            [KeyValuePair.Create(token.FormFieldName, token.Token)]);
        HttpResponseMessage response = await client.PostAsync(
            "/api/auth/logout",
            form);

        // No provider is configured here, so sign-out clears the cookie and
        // redirects straight to the login route instead of going by way of an
        // end-session endpoint. Either way the browser is sent somewhere.
        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Be("/login");
    }

    /// <summary>
    /// The endpoint moved from a <c>fetch</c> to a form post, which is exactly
    /// the change that could drop it out of antiforgery cover: a browser can be
    /// made to post a form cross-site, and only the token distinguishes this
    /// request from that one.
    /// </summary>
    [TestMethod]
    public async Task LogoutRejectsAFormPostCarryingNoToken()
    {
        using HttpClient client = CreateClient();
        await RequestTokenAsync(client);

        using FormUrlEncodedContent form = new FormUrlEncodedContent(
            Array.Empty<KeyValuePair<string, string>>());
        HttpResponseMessage response = await client.PostAsync(
            "/api/auth/logout",
            form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Sign-out is still an authenticated route. The client checks the session
    /// before submitting precisely so this answer never reaches a browser as a
    /// page, but the endpoint holds the line either way.
    /// </summary>
    [TestMethod]
    public async Task LogoutRejectsAnAnonymousRequest()
    {
        using HttpClient client = factory.CreateClient(ClientOptions());
        AntiforgeryTokenResponse token = await RequestTokenAsync(client);

        using FormUrlEncodedContent form = new FormUrlEncodedContent(
            [KeyValuePair.Create(token.FormFieldName, token.Token)]);
        HttpResponseMessage response = await client.PostAsync(
            "/api/auth/logout",
            form);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The client cannot build the form without being told which field name to
    /// use, so the token endpoint reports it alongside the header name.
    /// </summary>
    [TestMethod]
    public async Task AntiforgeryEndpointReportsTheFormFieldName()
    {
        using HttpClient client = CreateClient();

        AntiforgeryTokenResponse token = await RequestTokenAsync(client);

        token.FormFieldName.Should().NotBeNullOrWhiteSpace();
        token.HeaderName.Should().Be("X-CSRF-TOKEN");
    }

    private static WebApplicationFactoryClientOptions ClientOptions()
    {
        return new WebApplicationFactoryClientOptions
        {
            // The redirect is the assertion, so it must not be chased into a
            // login route this host has no client bundle to serve.
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        };
    }

    private static async Task<AntiforgeryTokenResponse> RequestTokenAsync(
        HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync("/api/auth/antiforgery");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>()
            ?? throw new InvalidOperationException(
                "The antiforgery endpoint returned no token.");
    }

    private HttpClient CreateClient()
    {
        HttpClient client = factory.CreateClient(ClientOptions());
        client.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.UserIdHeaderName,
            UserId.ToString());

        return client;
    }
}
