using System.Net.Http.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Sleeky.Todo.Api;
using Sleeky.Todo.Api.Contracts.Auth;

namespace Sleeky.Todo.IntegrationTests.Api;

internal sealed class TodoApiFactory : WebApplicationFactory<Program>
{
    public const string DevelopmentEnvironment = "Development";
    public const string TestingEnvironment = "Testing";

    private readonly string connectionString;
    private readonly string databaseName;
    private readonly string environmentName;

    public TodoApiFactory(
        string connectionString,
        string databaseName,
        string environmentName = TestingEnvironment)
    {
        this.connectionString = connectionString;
        this.databaseName = databaseName;
        this.environmentName = environmentName;
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

        AntiforgeryTokenResponse token = await client
            .GetFromJsonAsync<AntiforgeryTokenResponse>("/api/auth/antiforgery")
            ?? throw new InvalidOperationException(
                "The antiforgery endpoint returned no token.");
        client.DefaultRequestHeaders.Add(token.HeaderName, token.Token);

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.UseSetting("MongoDb:ConnectionString", connectionString);
        builder.UseSetting("MongoDb:DatabaseName", databaseName);
        builder.UseSetting("MongoDb:TodoItemsCollectionName", "todoItems");
        builder.UseSetting("MongoDb:UsersCollectionName", "users");

        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }
}
