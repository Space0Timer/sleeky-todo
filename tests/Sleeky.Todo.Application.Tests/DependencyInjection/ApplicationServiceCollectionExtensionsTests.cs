using FluentAssertions;

using FluentValidation;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.Behaviors;
using Sleeky.Todo.Application.DependencyInjection;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
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
        ServiceCollection services = new ServiceCollection();
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
        ServiceCollection services = new ServiceCollection();
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
}
