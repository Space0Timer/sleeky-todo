using Sleeky.Todo.Application.DependencyInjection;

namespace Sleeky.Todo.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services.AddApplication();
        builder.Services.AddControllers();

        WebApplication app = builder.Build();

        app.UseHttpsRedirection();
        app.MapControllers();

        app.Run();
    }
}
