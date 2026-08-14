using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Commands.BulkDeleteTodos;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.Bulk;

[TestClass]
public sealed class BulkDeleteTodosCommandHandlerTests
{
    private readonly ITodoRepository repository = Substitute.For<ITodoRepository>();
    private readonly ImmediateTransactionExecutor transactionExecutor =
        new ImmediateTransactionExecutor();

    private IReadOnlyCollection<TodoItem>? capturedUpdates;

    [TestInitialize]
    public void CaptureBatchWrites()
    {
        repository.SaveBatchAsync(
                Arg.Do<IReadOnlyCollection<TodoItem>>(updates => capturedUpdates = updates),
                Arg.Any<IReadOnlyCollection<TodoItem>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        repository.GetActiveDependentIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Guid>());
    }

    [TestMethod]
    public async Task DependentAndPrerequisiteAreDeletedTogether()
    {
        TodoItem prerequisite = TestTodoFactory.Create("prerequisite");
        TodoItem dependent = TestTodoFactory.Create("dependent");
        dependent.AddDependency(prerequisite.Id, TestTodoFactory.Timestamp);
        StageLoad(prerequisite, dependent);

        BulkTodoResult result = await HandleAsync(Select(prerequisite, dependent));

        result.Items.Should().OnlyContain(item => item.DeletedAt != null);
        result.Items.Select(item => item.Version).Should().AllBeEquivalentTo(2L);
        capturedUpdates.Should().HaveCount(2);
        await repository.Received(1).GetActiveDependentIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Contains(prerequisite.Id) && ids.Contains(dependent.Id)),
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Contains(prerequisite.Id) && ids.Contains(dependent.Id)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ActiveDependentLeftBehindRejectsTheBatch()
    {
        TodoItem prerequisite = TestTodoFactory.Create("prerequisite");
        StageLoad(prerequisite);
        repository.GetActiveDependentIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([TestTodoFactory.CreateId("dependent")]);

        Func<Task> act = () => HandleAsync(Select(prerequisite));

        await act.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("A TODO with active dependents cannot be deleted.");
        await repository.DidNotReceiveWithAnyArgs().SaveBatchAsync(
            default!,
            default!,
            default);
    }

    [TestMethod]
    public async Task MissingTodoRejectsTheBatch()
    {
        TodoItem present = TestTodoFactory.Create("todo-1");
        StageLoad(present);

        Func<Task> act = () => HandleAsync(
            Select(present),
            new BulkTodoItemRequest(TestTodoFactory.CreateId("missing"), 1));

        await act.Should().ThrowAsync<NotFoundException>();
        await repository.DidNotReceiveWithAnyArgs().SaveBatchAsync(
            default!,
            default!,
            default);
    }

    [TestMethod]
    public async Task StaleVersionRejectsTheBatch()
    {
        TodoItem todoItem = TestTodoFactory.Create("todo-1");
        StageLoad(todoItem);

        Func<Task> act = () => HandleAsync([new BulkTodoItemRequest(todoItem.Id, 4)]);

        BulkConcurrencyConflictException exception = (await act.Should()
            .ThrowAsync<BulkConcurrencyConflictException>())
            .Which;
        exception.ResourceIds.Should().Equal(todoItem.Id);
    }

    [TestMethod]
    public async Task DeletionTimestampsMatchSingleItemDeletion()
    {
        TodoItem todoItem = TestTodoFactory.Create("todo-1");
        StageLoad(todoItem);
        DateTimeOffset deletedAt = TestTodoFactory.Timestamp.AddHours(1);

        BulkTodoResult result = await HandleAsync(Select(todoItem));

        result.Items.Should().ContainSingle()
            .Which.DeletedAt.Should().Be(deletedAt);
        todoItem.PurgeAt.Should().Be(deletedAt.AddDays(90));
        todoItem.Status.Should().Be(TestTodoFactory.Create("todo-1").Status);
    }

    private static BulkTodoItemRequest[] Select(params TodoItem[] todos)
    {
        return todos
            .Select(todo => new BulkTodoItemRequest(todo.Id, todo.Version))
            .ToArray();
    }

    private void StageLoad(params TodoItem[] todos)
    {
        repository.GetByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(todos);
    }

    private Task<BulkTodoResult> HandleAsync(
        BulkTodoItemRequest[] selection,
        params BulkTodoItemRequest[] extra)
    {
        BulkTodoItemRequest[] items = selection.Concat(extra).ToArray();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestTodoFactory.Timestamp.AddHours(1));
        BulkDeleteTodosCommandHandler handler = new BulkDeleteTodosCommandHandler(
            repository,
            clock,
            transactionExecutor,
            NullLogger<BulkDeleteTodosCommandHandler>.Instance);

        return handler.Handle(
            new BulkDeleteTodosCommand(items),
            CancellationToken.None);
    }
}
