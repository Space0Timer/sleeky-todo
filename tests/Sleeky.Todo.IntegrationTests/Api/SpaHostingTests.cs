using System.Net;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// Covers the host serving the client from its own origin. Like the rest of
/// <see cref="ApiStartupTests"/> these need no database, container, or identity
/// provider, so they run on every commit. A stub client stands in for the real
/// build, because what is under test is the host's routing and not the bundle.
/// </summary>
[TestClass]
public sealed class SpaHostingTests
{
    private const string AssetFileName = "index-abc12345.js";
    private const string ShellMarker = "<div id=\"root\"></div>";

    private static string webRootPath = null!;

    private ApiStartupFactory factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext testContext)
    {
        webRootPath = Path.Combine(
            Path.GetTempPath(),
            $"sleeky-todo-webroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(webRootPath, "assets"));

        string shell = "<!doctype html><html><head>"
            + "<title>Sleeky To-Do</title></head>"
            + $"<body>{ShellMarker}</body></html>";

        File.WriteAllText(Path.Combine(webRootPath, "index.html"), shell);
        File.WriteAllText(
            Path.Combine(webRootPath, "assets", AssetFileName),
            "console.log('stub')");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(webRootPath))
        {
            Directory.Delete(webRootPath, true);
        }
    }

    [TestInitialize]
    public void TestInitialize()
    {
        factory = new ApiStartupFactory(webRootPath: webRootPath);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        factory?.Dispose();
    }

    /// <summary>
    /// The guard for this feature's sharpest edge. Endpoints without their own
    /// authorization metadata inherit a policy requiring an authenticated user,
    /// which answered the client shell with a 401 and left signing in
    /// impossible, because the sign-in page is a route inside the shell.
    /// </summary>
    [TestMethod]
    public async Task ClientShellIsServedToAnAnonymousVisitor()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("text/html");

        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(ShellMarker);
    }

    /// <summary>
    /// The client routes on browser history, so a bookmarked or refreshed deep
    /// link arrives at this host rather than at the client.
    /// </summary>
    [TestMethod]
    public async Task DeepLinkIsAnsweredWithTheClientShell()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/login");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(ShellMarker);
    }

    /// <summary>
    /// Answering an unknown API path with the shell would turn a caller's
    /// mistake into a parse error against a body of HTML.
    /// </summary>
    [TestMethod]
    public async Task UnknownApiRouteIsNotAnsweredWithTheClientShell()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/not-a-real-endpoint");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        string body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(ShellMarker);
    }

    /// <summary>
    /// A request for an asset that no longer exists is a 404 rather than the
    /// shell, so a stale client sees a failed script rather than HTML parsed
    /// as JavaScript.
    /// </summary>
    [TestMethod]
    public async Task MissingAssetIsNotAnsweredWithTheClientShell()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/assets/index-deadbeef.js");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task FingerprintedAssetIsCachedAndTheShellIsNot()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage asset = await client.GetAsync(
            $"/assets/{AssetFileName}");
        HttpResponseMessage shell = await client.GetAsync("/");

        asset.StatusCode.Should().Be(HttpStatusCode.OK);
        asset.Headers.CacheControl?.ToString()
            .Should().Contain("immutable");
        shell.Headers.CacheControl?.NoCache.Should().BeTrue();
    }

    /// <summary>
    /// Serving the client must not have opened the API to anonymous callers.
    /// </summary>
    [TestMethod]
    public async Task ProtectedApiRouteStillRequiresAuthentication()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/todos");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The client shell is the response these headers exist for, because it is
    /// the one a browser parses as a document.
    /// </summary>
    [TestMethod]
    public async Task ClientShellCarriesTheSecurityHeaders()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/");

        response.Headers.GetValues("X-Content-Type-Options")
            .Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("Referrer-Policy")
            .Should().ContainSingle().Which.Should().Be("no-referrer");
        response.Headers.GetValues("X-Frame-Options")
            .Should().ContainSingle().Which.Should().Be("DENY");
    }

    /// <summary>
    /// The directives the policy is actually for. A renderer added to the
    /// assistant panel is what would put stored TODO text in front of a script
    /// parser, and `script-src 'self'` without `unsafe-inline` is what keeps
    /// that from being the end of the story. Asserted individually so relaxing
    /// one of them has to be deliberate.
    /// </summary>
    [TestMethod]
    [DataRow("default-src 'self'")]
    [DataRow("script-src 'self'")]
    [DataRow("object-src 'none'")]
    [DataRow("frame-ancestors 'none'")]
    [DataRow("base-uri 'self'")]
    public async Task ContentSecurityPolicyCarriesItsDirectives(string directive)
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/");
        string policy = response.Headers
            .GetValues("Content-Security-Policy")
            .Single();

        policy.Should().Contain(directive);
        policy.Should().NotContain("unsafe-inline");
        policy.Should().NotContain("unsafe-eval");
    }

    /// <summary>
    /// An API response carries them too. A JSON body is not parsed as a
    /// document, but nosniff is what keeps a browser from deciding otherwise.
    /// </summary>
    [TestMethod]
    public async Task ApiResponsesCarryTheSecurityHeaders()
    {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/todos");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("X-Content-Type-Options")
            .Should().ContainSingle().Which.Should().Be("nosniff");
    }

    private HttpClient CreateClient()
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
    }
}
