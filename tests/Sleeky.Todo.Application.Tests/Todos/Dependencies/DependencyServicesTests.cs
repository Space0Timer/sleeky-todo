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
        DependencyCycleDetector detector = new DependencyCycleDetector(repository);

        bool createsCycle = await detector.WouldCreateCycleAsync(
            Id("todo-a"),
            Id("todo-b"));

        createsCycle.Should().BeTrue();
        await repository.Received(1).GetDependencyNodesAsync(
            Arg.Is<IEnumerable<Guid>>(ids =>
                ids.ToHashSet().SetEquals(new[] { Id("todo-c"), Id("todo-d") })),
            true,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A TODO in the trash keeps its edges and can be restored, so a path
    /// through it still counts. Skipping deleted nodes would let an edge be
    /// added that closes a cycle the moment the node comes back.
    /// </summary>
    [TestMethod]
    public async Task GraphServiceWalksThroughDeletedNodes()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        StubGraph(
            repository,
            CreateNode("todo-b", dependencies: new[] { "todo-c" }),
            CreateNode("todo-c", isDeleted: true, dependencies: new[] { "todo-a" }),
            CreateNode("todo-a"));
        DependencyCycleDetector detector = new DependencyCycleDetector(repository);

        bool createsCycle = await detector.WouldCreateCycleAsync(
            Id("todo-a"),
            Id("todo-b"));

        createsCycle.Should().BeTrue();
        await repository.DidNotReceive().GetDependencyNodesAsync(
            Arg.Any<IEnumerable<Guid>>(),
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
        DependencyCycleDetector detector = new DependencyCycleDetector(repository);

        bool createsCycle = await detector.WouldCreateCycleAsync(
            Id("todo-a"),
            Id("todo-b"));

        createsCycle.Should().BeFalse();
        await repository.Received(2).GetDependencyNodesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            true,
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
        DependencyCycleDetector detector = new DependencyCycleDetector(repository);

        Func<Task> deep = async () => await detector.WouldCreateCycleAsync(
            Id("unrelated"),
            Id("link-0"));

        await deep.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("The dependency graph is too deep to validate.");
    }

    /// <summary>
    /// A graph too wide to walk is refused on the level that breaches the node
    /// budget, not the one after it.
    /// </summary>
    /// <remarks>
    /// The single fan-out here is larger than the whole budget, so a cap applied
    /// only on the following pass would let the entire level be accumulated
    /// first — spending the memory the cap exists to refuse. Asserting that only
    /// one read happened is what pins the check ahead of that.
    /// </remarks>
    [TestMethod]
    public async Task GraphServiceRejectsAFanOutWiderThanTheNodeBudget()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        string[] children = Enumerable.Range(0, 20_000)
            .Select(index => $"child-{index}")
            .ToArray();
        StubGraph(repository, CreateNode("hub", dependencies: children));
        DependencyCycleDetector detector = new DependencyCycleDetector(repository);

        Func<Task> wide = async () => await detector.WouldCreateCycleAsync(
            Id("unrelated"),
            Id("hub"));

        await wide.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("The dependency graph is too large to validate.");
        await repository.Received(1).GetDependencyNodesAsync(
            Arg.Any<IEnumerable<Guid>>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
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
        TodoStatus status = TodoStatus.Open,
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
