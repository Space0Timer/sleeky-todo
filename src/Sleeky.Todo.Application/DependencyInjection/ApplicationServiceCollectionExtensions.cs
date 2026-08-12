using FluentValidation;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Sleeky.Todo.Application.Behaviors;
using Sleeky.Todo.Application.Todos.Dependencies;

namespace Sleeky.Todo.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly);
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(DomainRuleExceptionBehavior<,>));
        });
        services.AddScoped<IDependencyGraphService, DependencyGraphService>();
        services.AddScoped<ITodoDependencyEvaluator, TodoDependencyEvaluator>();

        return services;
    }
}
