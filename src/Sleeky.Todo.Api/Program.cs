using Serilog;

using Sleeky.Todo.Api.DependencyInjection;
using Sleeky.Todo.Application.DependencyInjection;
using Sleeky.Todo.Infrastructure.DependencyInjection;

namespace Sleeky.Todo.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting Sleeky To-Do API");
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSerilog((services, configuration) => configuration
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());

            builder.Services.AddApplication();
            builder.Services.AddInfrastructure(builder.Configuration);
            builder.Services.AddApi();

            WebApplication app = builder.Build();

            app.UseApi();
            app.Run();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Sleeky To-Do API terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
