using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Sleeky.Todo.Api.Contracts.Assistant;
using Sleeky.Todo.Api.Contracts.Auth;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Assistant.Turns;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// The limits one user runs into, at the host rather than at a handler.
/// </summary>
/// <remarks>
/// No database and no container: a refusal is decided before anything a request
/// would have loaded, and the requests that are allowed through here are the two
/// that touch no persistence — reading the current user and signing out. That
/// keeps this suite on every commit, which is where a limit that could lock a
/// user out of their own application belongs.
/// </remarks>
[TestClass]
public sealed class RateLimitingApiTests
{
    private const string LogoutPath = "/api/auth/logout";
    private const string TurnsPath = "/api/assistant/turns";
    private const string SettingsPath = "/api/assistant/settings";

    private static readonly Guid UserId =
        Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");

    private static readonly Guid OtherUserId =
        Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff");

    private static readonly Guid SpaceId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    private ApiStartupFactory factory = null!;

    [TestCleanup]
    public void TestCleanup()
    {
        factory?.Dispose();
    }

    [TestMethod]
    public async Task MutationsBeyondTheWindowAreRefused()
    {
        factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:MutationPermitLimit"] = "1",
        });
        HttpClient client = await CreateAuthenticatedClientAsync(UserId);

        using HttpResponseMessage first = await client.PostAsync(LogoutPath, null);
        using HttpResponseMessage second = await client.PostAsync(LogoutPath, null);

        first.StatusCode.Should().Be(HttpStatusCode.Found);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Paging a long list is the one thing a well-behaved client does in a tight
    /// loop, so a spent mutation budget must not stop a user reading.
    /// </summary>
    [TestMethod]
    public async Task ReadsAreNotCountedAgainstTheMutationLimit()
    {
        factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:MutationPermitLimit"] = "1",
        });
        HttpClient client = await CreateAuthenticatedClientAsync(UserId);

        using HttpResponseMessage spent = await client.PostAsync(LogoutPath, null);
        using HttpResponseMessage refused = await client.PostAsync(LogoutPath, null);
        using HttpResponseMessage read = await client.GetAsync("/api/auth/me");

        spent.StatusCode.Should().Be(HttpStatusCode.Found);
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A refusal is answered in the same shape as every other error, so a client
    /// reads it the way it reads the rest and the user can quote an identifier
    /// that appears in the logs.
    /// </summary>
    [TestMethod]
    public async Task RefusalUsesTheProblemContract()
    {
        factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:MutationPermitLimit"] = "1",
        });
        HttpClient client = await CreateAuthenticatedClientAsync(UserId);

        using HttpResponseMessage spent = await client.PostAsync(LogoutPath, null);
        using HttpResponseMessage refused = await client.PostAsync(LogoutPath, null);

        refused.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");

        using JsonDocument problem = JsonDocument.Parse(
            await refused.Content.ReadAsStringAsync());
        JsonElement root = problem.RootElement;

        root.GetProperty("status").GetInt32()
            .Should().Be((int)HttpStatusCode.TooManyRequests);
        root.GetProperty("title").GetString().Should().Be("Too many requests.");
        root.GetProperty("instance").GetString().Should().Be(LogoutPath);
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// One user exhausting a limit must not spend another user's.
    /// </summary>
    [TestMethod]
    public async Task LimitsArePartitionedByUser()
    {
        factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:MutationPermitLimit"] = "1",
        });
        HttpClient client = await CreateAuthenticatedClientAsync(UserId);
        HttpClient other = await CreateAuthenticatedClientAsync(OtherUserId);

        using HttpResponseMessage spent = await client.PostAsync(LogoutPath, null);
        using HttpResponseMessage refused = await client.PostAsync(LogoutPath, null);
        using HttpResponseMessage unaffected = await other.PostAsync(LogoutPath, null);

        spent.StatusCode.Should().Be(HttpStatusCode.Found);
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        unaffected.StatusCode.Should().Be(HttpStatusCode.Found);
    }

    /// <summary>
    /// The limit that matters: a turn holds its request open for as long as the
    /// model takes, so what is bounded is how many a user may have running at
    /// once. The stub runner blocks to hold the first one there.
    /// </summary>
    [TestMethod]
    public async Task AssistantTurnsBeyondThePermitAreRefused()
    {
        BlockingTurnRunner runner = new BlockingTurnRunner();
        factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["RateLimiting:AssistantTurnConcurrency"] = "1",
            },
            services =>
            {
                services.AddSingleton<IAssistantTurnRunner>(runner);

                // Stubbed for the same reason the runner is: this suite has no
                // database, and what it measures is the host's permit rather
                // than the Space check the turn also performs.
                services.AddSingleton<ISpaceAccessService, GrantingSpaceAccess>();
            });
        HttpClient client = await CreateAuthenticatedClientAsync(UserId);

        using HttpRequestMessage holding = BuildTurnRequest();
        using HttpResponseMessage held = await client.SendAsync(
            holding,
            HttpCompletionOption.ResponseHeadersRead);

        held.StatusCode.Should().Be(HttpStatusCode.OK);

        // The permit is taken when the request enters the pipeline, but the
        // second request only proves anything once the first is genuinely
        // inside the turn rather than still on its way there.
        await runner.Started.WaitAsync(TimeSpan.FromSeconds(10));

        using HttpRequestMessage extra = BuildTurnRequest();
        using HttpResponseMessage refused = await client.SendAsync(
            extra,
            HttpCompletionOption.ResponseHeadersRead);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        runner.Release();
    }

    /// <summary>
    /// Only the turn route carries its own policy. The assistant's settings
    /// routes are ordinary mutations — one of them opens an outbound connection
    /// to a host the user names — so they stay inside the mutation window
    /// rather than sharing the turn route's exemption.
    /// </summary>
    [TestMethod]
    public async Task AssistantSettingsMutationsCountAgainstTheMutationWindow()
    {
        factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["RateLimiting:MutationPermitLimit"] = "1",
            },
            services => services.AddSingleton<IAssistantSettingsRepository, EmptySettings>());
        HttpClient client = await CreateAuthenticatedClientAsync(UserId);

        using HttpResponseMessage first = await client.DeleteAsync(SettingsPath);
        using HttpResponseMessage second = await client.DeleteAsync(SettingsPath);

        first.StatusCode.Should().Be(HttpStatusCode.NotFound);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [TestMethod]
    public async Task LimitsCanBeTurnedOff()
    {
        factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "false",
            ["RateLimiting:MutationPermitLimit"] = "1",
        });
        HttpClient client = await CreateAuthenticatedClientAsync(UserId);

        using HttpResponseMessage first = await client.PostAsync(LogoutPath, null);
        using HttpResponseMessage second = await client.PostAsync(LogoutPath, null);

        first.StatusCode.Should().Be(HttpStatusCode.Found);
        second.StatusCode.Should().Be(HttpStatusCode.Found);
    }

    private static HttpRequestMessage BuildTurnRequest()
    {
        return new HttpRequestMessage(HttpMethod.Post, TurnsPath)
        {
            Content = JsonContent.Create(new AssistantTurnRequest
            {
                SpaceId = SpaceId,
                Message = "What is due today?",
            }),
        };
    }

    private static ApiStartupFactory CreateFactory(
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection>? configureServices = null)
    {
        return new ApiStartupFactory(
            settings: settings,
            configureServices: services =>
            {
                services
                    .AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });

                configureServices?.Invoke(services);
            });
    }

    /// <summary>
    /// Mirrors <see cref="TodoApiFactory.CreateAuthenticatedClientAsync"/>: the
    /// antiforgery token is requested after the user header is set, because a
    /// token is bound to the identity that asked for it.
    /// </summary>
    private async Task<HttpClient> CreateAuthenticatedClientAsync(Guid userId)
    {
        HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                // Sign-out answers with a redirect, and the status is what these
                // tests are reading. Following it would land on a login route
                // this host has no client bundle to serve, turning every
                // allowed request into a 404.
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });
        client.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.UserIdHeaderName,
            userId.ToString());

        AntiforgeryTokenResponse token = await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/auth/antiforgery")
            ?? throw new InvalidOperationException(
                "The antiforgery endpoint returned no token.");
        client.DefaultRequestHeaders.Add(token.HeaderName, token.Token);

        return client;
    }

    /// <summary>
    /// Holds a turn open until released, so a second turn meets a permit that is
    /// genuinely in use rather than one that has already been handed back.
    /// </summary>
    private sealed class BlockingTurnRunner : IAssistantTurnRunner
    {
        private readonly TaskCompletionSource started =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource release =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public void Release()
        {
            release.TrySetResult();
        }

        public async Task RunAsync(
            AssistantTurn turn,
            ITurnEventWriter events,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();

            // Cancellation is the disposal path: the suite releases the turn on
            // its way out, and the host cancels it if a test fails first.
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Grants every Space at Owner level, so a turn reaches the rate limiter
    /// rather than a database this suite deliberately does not have.
    /// </summary>
    private sealed class GrantingSpaceAccess : ISpaceAccessService
    {
        public Task<SpaceAccessContext> RequireAsync(
            Guid spaceId,
            SpacePermission requiredPermission,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new SpaceAccessContext(spaceId, "Test Space", SpacePermission.Owner));
        }
    }

    /// <summary>
    /// A settings store with nothing in it, so a settings route can be reached
    /// without the database this suite does not have; what the test measures
    /// is the limiter in front of the route, not the route itself.
    /// </summary>
    private sealed class EmptySettings : IAssistantSettingsRepository
    {
        public Task<AssistantSettingsRecord?> GetAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AssistantSettingsRecord?>(null);
        }

        public Task SaveAsync(
            AssistantSettingsRecord settings,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }
}
