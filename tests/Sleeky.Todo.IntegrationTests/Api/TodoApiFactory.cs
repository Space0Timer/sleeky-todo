using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Sleeky.Todo.Api;

namespace Sleeky.Todo.IntegrationTests.Api;

internal sealed class TodoApiFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString;
    private readonly string databaseName;

    public TodoApiFactory(string connectionString, string databaseName)
    {
        this.connectionString = connectionString;
        this.databaseName = databaseName;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("MongoDb:ConnectionString", connectionString);
        builder.UseSetting("MongoDb:DatabaseName", databaseName);
        builder.UseSetting("MongoDb:TodoItemsCollectionName", "todoItems");
    }
}
