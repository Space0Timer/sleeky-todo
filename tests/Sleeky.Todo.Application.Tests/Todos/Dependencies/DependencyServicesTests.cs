using FluentAssertions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Todos.Dependencies;

[TestClass]
public sealed class DependencyServicesTests
{
    [TestMethod]
    public async Task GraphServiceDetectsMultiLevelCycleUsingBatchedFrontiers()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        Dictionary<Guid, TodoItem> graph = new Dictionary<Guid, TodoItem>
        {
            [Id("todo-b")] = CreateTodo(
                "todo-b",
                dependencies: new[] { "todo-c", "todo-d" }),
            [Id("todo-c")] = CreateTodo(
                "todo-c",
                dependencies: new[] { "todo-a" }),
            [Id("todo-d")] = CreateTodo("todo-d"),
            [Id("todo-a")] = CreateTodo("todo-a"),
        };
        repository.GetByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<Guid>>()
                .Where(graph.ContainsKey)
                .Select(id => graph[id])
                .ToArray());
        DependencyGraphService service = new DependencyGraphService(repository);

        bool createsCycle = await service.WouldCreateCycleAsync(
            Id("todo-a"),
            Id("todo-b"));

        createsCycle.Should().BeTrue();
        await repository.Received(1).GetByIdsAsync(
            Arg.Is<IEnumerable<Guid>>(ids =>
                ids.ToHashSet().SetEquals(new[] { Id("todo-c"), Id("todo-d") })),
            false,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GraphServiceTerminatesWhenExistingGraphContainsCycle()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        Dictionary<Guid, TodoItem> graph = new Dictionary<Guid, TodoItem>
        {
            [Id("todo-b")] = CreateTodo(
                "todo-b",
                dependencies: new[] { "todo-c" }),
            [Id("todo-c")] = CreateTodo(
                "todo-c",
                dependencies: new[] { "todo-b" }),
        };
        repository.GetByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<Guid>>()
                .Where(graph.ContainsKey)
                .Select(id => graph[id])
                .ToArray());
        DependencyGraphService service = new DependencyGraphService(repository);

        bool createsCycle = await service.WouldCreateCycleAsync(
            Id("todo-a"),
            Id("todo-b"));

        createsCycle.Should().BeFalse();
        await repository.Received(2).GetByIdsAsync(
            Arg.Any<IEnumerable<Guid>>(),
            false,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task EvaluatorTreatsMissingDeletedArchivedAndIncompleteTodosAsBlocking()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        TodoItem completed = CreateTodo("completed", TodoStatus.Completed);
        TodoItem incomplete = CreateTodo("incomplete", TodoStatus.InProgress);
        TodoItem archived = CreateTodo("archived", TodoStatus.Archived);
        TodoItem deleted = CreateTodo("deleted", TodoStatus.Completed);
        deleted.SoftDelete(TestTodoFactory.Timestamp.AddDays(1));
        repository.GetByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns(new[] { completed, incomplete, archived, deleted });
        TodoDependencyEvaluator evaluator = new TodoDependencyEvaluator(repository);

        TodoDependencyState state = await evaluator.EvaluateAsync(
            [Id("completed"), Id("incomplete"), Id("archived"), Id("deleted"), Id("missing")]);

        state.IsBlocked.Should().BeTrue();
        state.IncompleteDependencyCount.Should().Be(4);
        await repository.Received(1).GetByIdsAsync(
            Arg.Any<IEnumerable<Guid>>(),
            true,
            Arg.Any<CancellationToken>());
    }

    private static TodoItem CreateTodo(
        string id,
        TodoStatus status = TodoStatus.NotStarted,
        IReadOnlyCollection<string>? dependencies = null)
    {
        return TodoItem.Rehydrate(
            Id(id),
            TestTodoFactory.OwnerId,
            id,
            null,
            TestTodoFactory.DueDate,
            status,
            TodoPriority.Medium,
            dependencies?.Select(Id).ToArray() ?? Array.Empty<Guid>(),
            null,
            null,
            null,
            1,
            TestTodoFactory.Timestamp,
            TestTodoFactory.Timestamp,
            null,
            null);
    }

    private static Guid Id(string value)
    {
        return TestTodoFactory.CreateId(value);
    }
}
