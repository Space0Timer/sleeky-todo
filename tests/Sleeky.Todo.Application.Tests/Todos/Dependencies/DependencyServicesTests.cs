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
        Dictionary<string, TodoItem> graph = new Dictionary<string, TodoItem>
        {
            ["todo-b"] = CreateTodo(
                "todo-b",
                dependencies: new[] { "todo-c", "todo-d" }),
            ["todo-c"] = CreateTodo(
                "todo-c",
                dependencies: new[] { "todo-a" }),
            ["todo-d"] = CreateTodo("todo-d"),
            ["todo-a"] = CreateTodo("todo-a"),
        };
        repository.GetByIdsAsync(
                Arg.Any<IEnumerable<string>>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<string>>()
                .Where(graph.ContainsKey)
                .Select(id => graph[id])
                .ToArray());
        DependencyGraphService service = new DependencyGraphService(repository);

        bool createsCycle = await service.WouldCreateCycleAsync(
            "todo-a",
            "todo-b");

        createsCycle.Should().BeTrue();
        await repository.Received(1).GetByIdsAsync(
            Arg.Is<IEnumerable<string>>(ids =>
                ids.ToHashSet().SetEquals(new[] { "todo-c", "todo-d" })),
            false,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GraphServiceTerminatesWhenExistingGraphContainsCycle()
    {
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        Dictionary<string, TodoItem> graph = new Dictionary<string, TodoItem>
        {
            ["todo-b"] = CreateTodo(
                "todo-b",
                dependencies: new[] { "todo-c" }),
            ["todo-c"] = CreateTodo(
                "todo-c",
                dependencies: new[] { "todo-b" }),
        };
        repository.GetByIdsAsync(
                Arg.Any<IEnumerable<string>>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<string>>()
                .Where(graph.ContainsKey)
                .Select(id => graph[id])
                .ToArray());
        DependencyGraphService service = new DependencyGraphService(repository);

        bool createsCycle = await service.WouldCreateCycleAsync(
            "todo-a",
            "todo-b");

        createsCycle.Should().BeFalse();
        await repository.Received(2).GetByIdsAsync(
            Arg.Any<IEnumerable<string>>(),
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
                Arg.Any<IEnumerable<string>>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns(new[] { completed, incomplete, archived, deleted });
        TodoDependencyEvaluator evaluator = new TodoDependencyEvaluator(repository);

        TodoDependencyState state = await evaluator.EvaluateAsync(
            ["completed", "incomplete", "archived", "deleted", "missing"]);

        state.IsBlocked.Should().BeTrue();
        state.IncompleteDependencyCount.Should().Be(4);
        await repository.Received(1).GetByIdsAsync(
            Arg.Any<IEnumerable<string>>(),
            true,
            Arg.Any<CancellationToken>());
    }

    private static TodoItem CreateTodo(
        string id,
        TodoStatus status = TodoStatus.NotStarted,
        IReadOnlyCollection<string>? dependencies = null)
    {
        return TodoItem.Rehydrate(
            id,
            id,
            null,
            TestTodoFactory.DueDate,
            status,
            TodoPriority.Medium,
            dependencies ?? Array.Empty<string>(),
            null,
            null,
            null,
            1,
            TestTodoFactory.Timestamp,
            TestTodoFactory.Timestamp,
            null,
            null);
    }
}
