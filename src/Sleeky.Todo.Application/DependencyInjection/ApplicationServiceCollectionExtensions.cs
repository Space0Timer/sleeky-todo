using FluentValidation;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Sleeky.Todo.Application.Abstractions.Events;
using Sleeky.Todo.Application.Behaviors;
using Sleeky.Todo.Application.Events;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Application.Todos.Events;
using Sleeky.Todo.Domain.Events;
using Sleeky.Todo.Domain.Services;

namespace Sleeky.Todo.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();
        services.AddValidatorsFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly);
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
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<
            IDomainEventHandler<TodoCompletedDomainEvent>,
            CreateNextRecurringOccurrenceHandler>();

        return services;
    }
}
