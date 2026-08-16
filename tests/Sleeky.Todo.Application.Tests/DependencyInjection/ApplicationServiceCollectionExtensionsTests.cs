using FluentAssertions;

using FluentValidation;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.Behaviors;
using Sleeky.Todo.Application.DependencyInjection;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
using Sleeky.Todo.Application.Todos.Recurrence;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.DependencyInjection;

[TestClass]
public sealed class ApplicationServiceCollectionExtensionsTests
{
    [TestMethod]
    public async Task AddApplicationPreventsInvalidRequestFromReachingHandler()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        ServiceCollection services = CreateServicesWithSpaceAccessDoubles();
        services.AddSingleton(repository);
        services.AddSingleton(clock);
        services.AddApplication();
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        ISender sender = serviceProvider.GetRequiredService<ISender>();
        CreateTodoCommand invalidCommand = new CreateTodoCommand(
            "   ",
            null,
            default,
            (TodoPriority)999);

        Func<Task> act = async () =>
            await sender.Send(invalidCommand, CancellationToken.None);

        ValidationException exception = (await act.Should()
            .ThrowAsync<ValidationException>())
            .Which;
        exception.Errors.Should().HaveCount(3);
        await repository.DidNotReceiveWithAnyArgs().AddAsync(
            default!,
            default);
    }

    [TestMethod]
    public void AddApplicationRegistersValidatorsAndValidationBehavior()
    {
        ServiceCollection services = CreateServicesWithSpaceAccessDoubles();
        services.AddApplication();
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        IEnumerable<IValidator<CreateTodoCommand>> validators = serviceProvider
            .GetServices<IValidator<CreateTodoCommand>>();
        IEnumerable<IPipelineBehavior<CreateTodoCommand, TodoDto>> behaviors = serviceProvider
            .GetServices<IPipelineBehavior<CreateTodoCommand, TodoDto>>();

        validators.Should().ContainSingle(validator => validator is CreateTodoCommandValidator);
        behaviors.Should().ContainSingle(behavior =>
            behavior is ValidationBehavior<CreateTodoCommand, TodoDto>);
    }

    /// <summary>
    /// The access check has to run after validation — so a request naming no
    /// Space is a 400, not a lookup of the empty identifier — and it has to
    /// run at all, for every request type, which is why it is a pipeline
    /// behavior rather than a call each handler remembers to make.
    /// </summary>
    [TestMethod]
    public void AddApplicationRegistersTheSpaceAccessBehaviorAfterValidation()
    {
        ServiceCollection services = CreateServicesWithSpaceAccessDoubles();
        services.AddApplication();
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        List<IPipelineBehavior<CreateTodoCommand, TodoDto>> behaviors = serviceProvider
            .GetServices<IPipelineBehavior<CreateTodoCommand, TodoDto>>()
            .ToList();

        int validationIndex = behaviors.FindIndex(behavior =>
            behavior is ValidationBehavior<CreateTodoCommand, TodoDto>);
        int accessIndex = behaviors.FindIndex(behavior =>
            behavior is SpaceAccessBehavior<CreateTodoCommand, TodoDto>);
        accessIndex.Should().BeGreaterThan(validationIndex);
    }

    /// <summary>
    /// The holder the access service binds and the view persistence reads must
    /// be one object per request, or a check would bind a scope nobody reads.
    /// </summary>
    [TestMethod]
    public void AddApplicationRegistersOneSpaceScopePerRequest()
    {
        ServiceCollection services = CreateServicesWithSpaceAccessDoubles();
        services.AddApplication();
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        using IServiceScope firstRequest = serviceProvider.CreateScope();
        using IServiceScope secondRequest = serviceProvider.CreateScope();

        SpaceScope holder = firstRequest.ServiceProvider.GetRequiredService<SpaceScope>();
        ISpaceScope view = firstRequest.ServiceProvider.GetRequiredService<ISpaceScope>();
        ISpaceScope other = secondRequest.ServiceProvider.GetRequiredService<ISpaceScope>();
        ISpaceAccessService accessService = firstRequest.ServiceProvider
            .GetRequiredService<ISpaceAccessService>();

        view.Should().BeSameAs(holder);
        other.Should().NotBeSameAs(holder);
        accessService.Should().BeOfType<SpaceAccessService>();
    }

    /// <summary>
    /// The status handler builds a recurring successor itself, so the factory it
    /// depends on has to be registered; an unregistered one fails at resolution
    /// rather than silently skipping the next occurrence.
    /// </summary>
    [TestMethod]
    public void AddApplicationRegistersTheRecurringOccurrenceFactory()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddApplication();
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        IRecurringOccurrenceFactory factory = serviceProvider
            .GetRequiredService<IRecurringOccurrenceFactory>();

        factory.Should().BeOfType<RecurringOccurrenceFactory>();
    }

    /// <summary>
    /// The access behavior sits in every request's pipeline, so resolving any
    /// handler now needs the Space repository and the current user that the
    /// access service is built from — Infrastructure supplies both in the
    /// host, and these doubles stand in for it here.
    /// </summary>
    private static ServiceCollection CreateServicesWithSpaceAccessDoubles()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISpaceRepository>());
        services.AddSingleton<ICurrentUser>(new TestCurrentUser(Guid.NewGuid()));

        return services;
    }
}
