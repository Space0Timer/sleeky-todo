using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sleeky.Todo.Assistant.Conflicts;
using Sleeky.Todo.Assistant.Providers;
using Sleeky.Todo.Assistant.Turns;

namespace Sleeky.Todo.Assistant.DependencyInjection;

public static class AssistantServiceCollectionExtensions
{
    public static IServiceCollection AddAssistant(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Not validated on start: an application-level key is optional, and a
        // deployment where every user brings their own is a valid one.
        services
            .AddOptions<AssistantOptions>()
            .Bind(configuration.GetSection(AssistantOptions.SectionName));

        services.AddSingleton<AssistantKeyProtector>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IAssistantConnectionProbe, AssistantConnectionProbe>();
        services.AddScoped<IAssistantSettingsService, AssistantSettingsService>();
        services.AddScoped<IBulkConflictPolicy, BulkConflictPolicy>();
        services.AddScoped<IAssistantTurnRunner, AssistantTurnRunner>();

        return services;
    }
}
