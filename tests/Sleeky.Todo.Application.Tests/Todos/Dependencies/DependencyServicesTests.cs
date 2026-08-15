using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Tests.Todos.Dependencies;

[TestClass]
public sealed class DependencyServicesTests
{
    [TestMethod]
    public async Task GraphServiceDetectsMultiLevelCycleUsingBatchedFrontiers()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        StubGraph(
            repository,
            CreateNode("todo-b", dependencies: new[] { "todo-c", "todo-d" }),
            CreateNode("todo-c", dependencies: new[] { "todo-a" }),
            CreateNode("todo-d"),
            CreateNode("todo-a"));
        DependencyGraphService service = new DependencyGraphService(repository);

        bool createsCycle = await service.WouldCreateCycleAsync(
            Id("todo-a"),
            Id("todo-b"));

        createsCycle.Should().BeTrue();
        await repository.Received(1).GetDependencyNodesAsync(
            Arg.Is<IEnumerable<Guid>>(ids =>
                ids.ToHashSet().SetEquals(new[] { Id("todo-c"), Id("todo-d") })),
            false,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GraphServiceTerminatesWhenExistingGraphContainsCycle()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        StubGraph(
            repository,
            CreateNode("todo-b", dependencies: new[] { "todo-c" }),
            CreateNode("todo-c", dependencies: new[] { "todo-b" }));
        DependencyGraphService service = new DependencyGraphService(repository);

        bool createsCycle = await service.WouldCreateCycleAsync(
            Id("todo-a"),
            Id("todo-b"));

        createsCycle.Should().BeFalse();
        await repository.Received(2).GetDependencyNodesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            false,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A chain longer than the traversal bound is reported as a conflict rather
    /// than walked to the end.
    /// </summary>
    [TestMethod]
    public async Task GraphServiceRejectsAChainDeeperThanTheTraversalBound()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        TodoDependencyNode[] chain = Enumerable.Range(0, 200)
            .Select(index => CreateNode(
                $"link-{index}",
                dependencies: new[] { $"link-{index + 1}" }))
            .ToArray();
        StubGraph(repository, chain);
        DependencyGraphService service = new DependencyGraphService(repository);

        Func<Task> deep = async () => await service.WouldCreateCycleAsync(
            Id("unrelated"),
            Id("link-0"));

        await deep.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("The dependency graph is too deep to validate.");
    }

    [TestMethod]
    public async Task EvaluatorTreatsMissingDeletedArchivedAndIncompleteTodosAsBlocking()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        repository.GetDependencyNodesAsync(
                Arg.Any<IEnumerable<Guid>>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                CreateNode("completed", TodoStatus.Completed),
                CreateNode("incomplete", TodoStatus.InProgress),
                CreateNode("archived", TodoStatus.Archived),
                CreateNode("deleted", TodoStatus.Completed, isDeleted: true),
            });
        TodoDependencyEvaluator evaluator = new TodoDependencyEvaluator(repository);

        TodoDependencyState state = await evaluator.EvaluateAsync(
            [Id("completed"), Id("incomplete"), Id("archived"), Id("deleted"), Id("missing")]);

        state.IsBlocked.Should().BeTrue();
        state.IncompleteDependencyCount.Should().Be(4);
        await repository.Received(1).GetDependencyNodesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            true,
            Arg.Any<CancellationToken>());
    }

    private static void StubGraph(
        ITodoRepository repository,
        params TodoDependencyNode[] nodes)
    {
        Dictionary<Guid, TodoDependencyNode> graph = nodes.ToDictionary(node => node.Id);
        repository.GetDependencyNodesAsync(
                Arg.Any<IEnumerable<Guid>>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<Guid>>()
                .Where(graph.ContainsKey)
                .Select(id => graph[id])
                .ToArray());
    }

    private static TodoDependencyNode CreateNode(
        string id,
        TodoStatus status = TodoStatus.NotStarted,
        bool isDeleted = false,
        IReadOnlyCollection<string>? dependencies = null)
    {
        return new TodoDependencyNode(
            Id(id),
            status,
            isDeleted,
            dependencies?.Select(Id).ToArray() ?? Array.Empty<Guid>());
    }

    private static Guid Id(string value)
    {
        return TestTodoFactory.CreateId(value);
    }
}
