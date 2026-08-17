using System.Net.Http.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Sleeky.Todo.Api;
using Sleeky.Todo.Api.Contracts.Auth;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.IntegrationTests.Api;

internal sealed class TodoApiFactory : WebApplicationFactory<Program>
{
    public const string DevelopmentEnvironment = "Development";
    public const string TestingEnvironment = "Testing";

    private readonly string connectionString;
    private readonly string databaseName;
    private readonly string environmentName;
    private readonly Action<IServiceCollection>? configureServices;

    /// <summary>
    /// <paramref name="configureServices"/> runs after the host's own
    /// registrations, so a suite can replace a service the production graph
    /// resolves — the assistant's provider client, for instance, which cannot
    /// be reached in a test.
    /// </summary>
    public TodoApiFactory(
        string connectionString,
        string databaseName,
        string environmentName = TestingEnvironment,
        Action<IServiceCollection>? configureServices = null)
    {
        this.connectionString = connectionString;
        this.databaseName = databaseName;
        this.environmentName = environmentName;
        this.configureServices = configureServices;
    }

    /// <summary>
    /// Creates a client authenticated as <paramref name="userId"/> and carrying
    /// a matching antiforgery token. The token is requested after the user
    /// header is set because antiforgery tokens are bound to the identity.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(Guid userId)
    {
        HttpClient client = CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
        client.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.UserIdHeaderName,
            userId.ToString());

        HttpResponseMessage tokenResponse = await client.GetAsync(
            "/api/auth/antiforgery");
        if (!tokenResponse.IsSuccessStatusCode)
        {
            string body = await tokenResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"The antiforgery endpoint returned {(int)tokenResponse.StatusCode}: {body}");
        }

        AntiforgeryTokenResponse token = await tokenResponse.Content
            .ReadFromJsonAsync<AntiforgeryTokenResponse>()
            ?? throw new InvalidOperationException(
                "The antiforgery endpoint returned no token.");
        client.DefaultRequestHeaders.Add(token.HeaderName, token.Token);

        return client;
    }

    /// <summary>
    /// Seeds a Space owned by <paramref name="ownerUserId"/> and returns its
    /// identifier, which every TODO route is then nested under.
    /// </summary>
    /// <remarks>
    /// Written through the repository from the host's own container rather
    /// than through the Space API, so the TODO suites do not depend on those
    /// endpoints existing to have somewhere to put a TODO.
    /// </remarks>
    public async Task<Guid> CreateSpaceAsync(Guid ownerUserId, string name = "Test Space")
    {
        Guid spaceId = Guid.NewGuid();
        Space space = Space.Create(spaceId, name, ownerUserId, DateTimeOffset.UtcNow);

        using IServiceScope scope = Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<ISpaceRepository>()
            .AddAsync(space);

        return spaceId;
    }

    /// <summary>
    /// Grants <paramref name="userId"/> the given level in an existing Space,
    /// which is how a suite builds the membership table a permission scenario
    /// needs.
    /// </summary>
    public async Task GrantAsync(Guid spaceId, Guid userId, SpacePermission permission)
    {
        using IServiceScope scope = Services.CreateScope();
        ISpaceRepository spaces = scope.ServiceProvider
            .GetRequiredService<ISpaceRepository>();
        Space space = await spaces.GetByIdAsync(spaceId)
            ?? throw new InvalidOperationException($"Space '{spaceId}' should exist.");

        space.AddAccess(userId, SubjectType.User, permission, DateTimeOffset.UtcNow);
        _ = await spaces.UpdateAsync(space);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.UseSetting("MongoDb:ConnectionString", connectionString);
        builder.UseSetting("MongoDb:DatabaseName", databaseName);
        builder.UseSetting("MongoDb:TodoItemsCollectionName", "todoItems");
        builder.UseSetting("MongoDb:UsersCollectionName", "users");
        builder.UseSetting("MongoDb:SpacesCollectionName", "spaces");

        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });

            configureServices?.Invoke(services);
        });
    }
}
