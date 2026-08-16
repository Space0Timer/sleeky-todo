using FluentValidation;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Behaviors;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Application.Todos.Recurrence;
using Sleeky.Todo.Domain.Services;

namespace Sleeky.Todo.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();
        services.AddValidatorsFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly);

        // The assembly scan registers every request handler in this assembly.
        // Nothing registers notification handlers: no domain event is published.
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly);
            configuration.AddOpenBehavior(typeof(RequestLoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));

            // After validation, so a request naming no Space is a 400 rather
            // than a lookup of the empty identifier; before every handler.
            configuration.AddOpenBehavior(typeof(SpaceAccessBehavior<,>));
            configuration.AddOpenBehavior(typeof(DomainRuleExceptionBehavior<,>));
        });

        // One scope instance per request, reachable both as the holder the
        // access service binds and as the read-only view persistence consumes.
        services.AddScoped<SpaceScope>();
        services.AddScoped<ISpaceScope>(serviceProvider => serviceProvider.GetRequiredService<SpaceScope>());
        services.AddScoped<ISpaceAccessService, SpaceAccessService>();
        services.AddScoped<IDependencyCycleDetector, DependencyCycleDetector>();
        services.AddScoped<ITodoDependencyEvaluator, TodoDependencyEvaluator>();
        services.AddSingleton<IRecurrenceCalculator, RecurrenceCalculator>();
        services.AddSingleton<IRecurringOccurrenceFactory, RecurringOccurrenceFactory>();

        return services;
    }
}
