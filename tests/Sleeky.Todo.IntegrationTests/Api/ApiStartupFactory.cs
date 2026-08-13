using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sleeky.Todo.Api;
using Sleeky.Todo.Infrastructure.DependencyInjection;

namespace Sleeky.Todo.IntegrationTests.Api;

/// <summary>
/// Boots the production host configuration without a MongoDB server behind it.
/// The persistence-backed hosted services are the only thing removed, because
/// they connect while the host starts; the controller filters, authentication,
/// authorization, and error handling are all the real registrations.
/// </summary>
internal sealed class ApiStartupFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// A short server-selection timeout keeps the health endpoint honest
    /// without paying the driver's thirty second default to learn that no
    /// database is listening.
    /// </summary>
    private const string UnreachableConnectionString =
        "mongodb://localhost:27017/?serverSelectionTimeoutMS=250";

    private readonly string environmentName;
    private readonly string? webRootPath;

    public ApiStartupFactory(
        string environmentName = TodoApiFactory.TestingEnvironment,
        string? webRootPath = null)
    {
        this.environmentName = environmentName;
        this.webRootPath = webRootPath;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);

        // The API project carries no wwwroot of its own; the client is copied
        // in when the image is built. A suite that needs one points the host at
        // a directory it prepared.
        if (webRootPath is not null)
        {
            builder.UseWebRoot(webRootPath);
        }

        builder.UseSetting("MongoDb:ConnectionString", UnreachableConnectionString);
        builder.UseSetting("MongoDb:DatabaseName", "sleekyTodoStartupTests");
        builder.UseSetting("MongoDb:TodoItemsCollectionName", "todoItems");
        builder.UseSetting("MongoDb:UsersCollectionName", "users");

        builder.ConfigureTestServices(services =>
        {
            // Matched by assembly rather than by name so that a hosted service
            // added to persistence later is dropped here too. Nothing else in
            // the host starts by talking to the database.
            ServiceDescriptor[] persistenceHostedServices = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType?.Assembly
                        == typeof(InfrastructureServiceCollectionExtensions).Assembly)
                .ToArray();

            foreach (ServiceDescriptor descriptor in persistenceHostedServices)
            {
                services.Remove(descriptor);
            }
        });
    }
}
