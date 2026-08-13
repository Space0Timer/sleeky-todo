using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

using Sleeky.Todo.Api.Contracts.Auth;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// Proves the host boots and answers requests, with no Docker, no MongoDB, and
/// no identity provider, so these run on every commit and in every environment.
/// A host that compiles and even builds its container but cannot serve a
/// request — a filter it cannot construct, a service it cannot resolve — fails
/// here instead of on the first request after a deployment.
/// </summary>
[TestClass]
public sealed class ApiStartupTests
{
    private ApiStartupFactory factory = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        factory = new ApiStartupFactory();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        factory?.Dispose();
    }

    /// <summary>
    /// An anonymous GET through the full controller pipeline. Every action is
    /// built with the global antiforgery filter, so a filter the host cannot
    /// construct fails this test rather than every request in production.
    /// </summary>
    [TestMethod]
    public async Task CurrentUserEndpointAnswersAnonymousRequest()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        CurrentUserResponse? body = await response.Content
            .ReadFromJsonAsync<CurrentUserResponse>();

        body.Should().NotBeNull();
        body!.IsAuthenticated.Should().BeFalse();
        body.UserId.Should().BeNull();
    }

    /// <summary>
    /// The unsafe-method path through the same filters. Without credentials the
    /// answer is a 401 from the authorization policy; the point is that the
    /// request is answered at all rather than failing while its filters are
    /// assembled.
    /// </summary>
    [TestMethod]
    public async Task ProtectedEndpointRejectsAnonymousPostWithoutServerError()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/todos",
            new { name = "startup probe" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Health reports the database it cannot reach, which is a 503. A 500 here
    /// would mean the endpoint itself failed rather than the dependency.
    /// </summary>
    [TestMethod]
    public async Task HealthEndpointReportsRatherThanFailing()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        // A developer with the compose stack running has a reachable database
        // on the same address, so both answers are correct here.
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Swagger is registered in development only, and the host is asked for it
    /// there because a broken document is otherwise found by hand.
    /// </summary>
    [TestMethod]
    public async Task SwaggerDocumentIsServedInDevelopment()
    {
        using ApiStartupFactory developmentFactory = new ApiStartupFactory(
            TodoApiFactory.DevelopmentEnvironment);
        using HttpClient client = developmentFactory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient CreateClient()
    {
        // The host redirects to HTTPS outside development, so a plain HTTP base
        // address would measure the redirect rather than the endpoint.
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
    }
}
