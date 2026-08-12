using Sleeky.Todo.Api.DependencyInjection;
using Sleeky.Todo.Application.DependencyInjection;
using Sleeky.Todo.Infrastructure.DependencyInjection;

namespace Sleeky.Todo.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddApi();

        WebApplication app = builder.Build();

        app.UseExceptionHandler();
        app.UseHttpsRedirection();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.MapControllers();

        app.Run();
    }
}
