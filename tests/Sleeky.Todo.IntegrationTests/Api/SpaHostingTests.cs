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

    private HttpClient CreateClient()
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
    }
}
