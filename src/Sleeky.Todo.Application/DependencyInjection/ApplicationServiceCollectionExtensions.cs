using FluentValidation;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Sleeky.Todo.Application.Behaviors;
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
            configuration.AddOpenBehavior(typeof(DomainRuleExceptionBehavior<,>));
        });
        services.AddScoped<IDependencyGraphService, DependencyGraphService>();
        services.AddScoped<ITodoDependencyEvaluator, TodoDependencyEvaluator>();
        services.AddSingleton<IRecurrenceCalculator, RecurrenceCalculator>();
        services.AddSingleton<IRecurringOccurrenceFactory, RecurringOccurrenceFactory>();

        return services;
    }
}
