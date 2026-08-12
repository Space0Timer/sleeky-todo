using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.AddDependency;
using Sleeky.Todo.Application.Todos.Commands.RemoveDependency;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.Dependencies;

[TestClass]
public sealed class DependencyCommandHandlerTests
{
    [TestMethod]
    public async Task AddDependencyPersistsThroughOptimisticVersionPath()
    {
        TodoItem source = TestTodoFactory.Create("source");
        TodoItem dependency = TestTodoFactory.Create("dependency");
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IDependencyGraphService graph = Substitute.For<IDependencyGraphService>();
        IClock clock = Substitute.For<IClock>();
        DateTimeOffset updatedAt = TestTodoFactory.Timestamp.AddHours(1);
        clock.UtcNow.Returns(updatedAt);
        repository.GetByIdAsync(source.Id, false, Arg.Any<CancellationToken>())
            .Returns(source);
        repository.GetByIdAsync(dependency.Id, false, Arg.Any<CancellationToken>())
            .Returns(dependency);
        graph.WouldCreateCycleAsync(
                source.Id,
                dependency.Id,
                Arg.Any<CancellationToken>())
            .Returns(false);
        repository.UpdateAsync(source, 1, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(source, 2));
        AddDependencyCommandHandler handler = new AddDependencyCommandHandler(
            repository,
            graph,
            clock,
            NullLogger<AddDependencyCommandHandler>.Instance);

        TodoDto result = await handler.Handle(
            new AddDependencyCommand(source.Id, dependency.Id, 1),
            CancellationToken.None);

        result.DependencyIds.Should().Equal(dependency.Id);
        result.Version.Should().Be(2);
        result.UpdatedAt.Should().Be(updatedAt);
        await repository.Received(1).UpdateAsync(
            source,
            1,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AddDependencyRejectsMissingTargetAndCycle()
    {
        TodoItem source = TestTodoFactory.Create("source");
        TodoItem dependency = TestTodoFactory.Create("dependency");
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IDependencyGraphService graph = Substitute.For<IDependencyGraphService>();
        IClock clock = Substitute.For<IClock>();
        repository.GetByIdAsync(source.Id, false, Arg.Any<CancellationToken>())
            .Returns(source);
        repository.GetByIdAsync(
                TestTodoFactory.CreateId("missing"),
                false,
                Arg.Any<CancellationToken>())
            .Returns((TodoItem?)null);
        repository.GetByIdAsync(dependency.Id, false, Arg.Any<CancellationToken>())
            .Returns(dependency);
        graph.WouldCreateCycleAsync(
                source.Id,
                dependency.Id,
                Arg.Any<CancellationToken>())
            .Returns(true);
        AddDependencyCommandHandler handler = new AddDependencyCommandHandler(
            repository,
            graph,
            clock,
            NullLogger<AddDependencyCommandHandler>.Instance);

        Func<Task> missing = async () => await handler.Handle(
            new AddDependencyCommand(source.Id, TestTodoFactory.CreateId("missing"), 1),
            CancellationToken.None);
        Func<Task> cycle = async () => await handler.Handle(
            new AddDependencyCommand(source.Id, dependency.Id, 1),
            CancellationToken.None);

        await missing.Should().ThrowAsync<NotFoundException>();
        await cycle.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("Adding this dependency would create a cycle.");
    }

    [TestMethod]
    public async Task AddDependencyRejectsSelfAndStaleVersion()
    {
        TodoItem source = TestTodoFactory.Create("source");
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IDependencyGraphService graph = Substitute.For<IDependencyGraphService>();
        IClock clock = Substitute.For<IClock>();
        repository.GetByIdAsync(source.Id, false, Arg.Any<CancellationToken>())
            .Returns(source);
        AddDependencyCommandHandler handler = new AddDependencyCommandHandler(
            repository,
            graph,
            clock,
            NullLogger<AddDependencyCommandHandler>.Instance);

        Func<Task> stale = async () => await handler.Handle(
            new AddDependencyCommand(
                source.Id,
                TestTodoFactory.CreateId("dependency"),
                2),
            CancellationToken.None);
        Func<Task> self = async () => await handler.Handle(
            new AddDependencyCommand(source.Id, source.Id, 1),
            CancellationToken.None);

        await stale.Should().ThrowAsync<ConcurrencyConflictException>();
        await self.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("A TODO cannot depend on itself.");
    }

    [TestMethod]
    public async Task RemoveDependencyPersistsAndRejectsMissingDependency()
    {
        TodoItem source = TestTodoFactory.Create("source");
        source.AddDependency(TestTodoFactory.CreateId("dependency"), TestTodoFactory.Timestamp);
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        IClock clock = Substitute.For<IClock>();
        repository.GetByIdAsync(source.Id, false, Arg.Any<CancellationToken>())
            .Returns(source);
        repository.UpdateAsync(source, 1, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(source, 2));
        RemoveDependencyCommandHandler handler = new RemoveDependencyCommandHandler(
            repository,
            clock,
            NullLogger<RemoveDependencyCommandHandler>.Instance);

        TodoDto result = await handler.Handle(
            new RemoveDependencyCommand(source.Id, TestTodoFactory.CreateId("dependency"), 1),
            CancellationToken.None);

        result.DependencyIds.Should().BeEmpty();
        result.Version.Should().Be(2);

        TodoItem sourceWithoutDependencies = TestTodoFactory.Create("other-source");
        repository.GetByIdAsync(
                sourceWithoutDependencies.Id,
                false,
                Arg.Any<CancellationToken>())
            .Returns(sourceWithoutDependencies);
        Func<Task> missing = async () => await handler.Handle(
            new RemoveDependencyCommand(
                sourceWithoutDependencies.Id,
                TestTodoFactory.CreateId("missing"),
                1),
            CancellationToken.None);
        await missing.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("The TODO dependency does not exist.");
    }
}
