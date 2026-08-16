using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Commands.Bulk.ChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Recurrence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.Services;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.Bulk.ChangeTodoStatus;

[TestClass]
public sealed class BulkChangeTodoStatusCommandHandlerTests
{
    private readonly ITodoRepository repository = Substitute.For<ITodoRepository>();
    private readonly ImmediateTransactionExecutor transactionExecutor =
        new ImmediateTransactionExecutor();

    private IReadOnlyCollection<TodoItem>? capturedUpdates;
    private IReadOnlyCollection<TodoItem>? capturedInserts;

    [TestInitialize]
    public void CaptureBatchWrites()
    {
        repository.SaveBatchAsync(
                Arg.Do<IReadOnlyCollection<TodoItem>>(updates => capturedUpdates = updates),
                Arg.Do<IReadOnlyCollection<TodoItem>>(inserts => capturedInserts = inserts),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    [TestMethod]
    public async Task MissingTodoRejectsTheBatch()
    {
        TodoItem present = TestTodoFactory.Create("todo-1");
        StageLoad(present);

        Func<Task> act = () => HandleAsync(
            TodoStatus.Completed,
            Select(present),
            new BulkTodoItemRequest(TestTodoFactory.CreateId("missing"), 1));

        NotFoundException exception = (await act.Should().ThrowAsync<NotFoundException>())
            .Which;
        exception.ResourceId.Should().Be(TestTodoFactory.CreateId("missing"));
        await AssertNoWriteAsync();
    }

    [TestMethod]
    public async Task StaleVersionRejectsTheBatchAndNamesTheOffendingTodos()
    {
        TodoItem current = TestTodoFactory.Create("todo-1");
        TodoItem stale = TestTodoFactory.Create("todo-2");
        StageLoad(current, stale);

        Func<Task> act = () => HandleAsync(
            TodoStatus.Completed,
            Select(current),
            new BulkTodoItemRequest(stale.Id, 7));

        BulkConcurrencyConflictException exception = (await act.Should()
            .ThrowAsync<BulkConcurrencyConflictException>())
            .Which;
        exception.ResourceIds.Should().Equal(stale.Id);
        await AssertNoWriteAsync();
    }

    [TestMethod]
    public async Task IndependentTodosAreCompletedInOneBatch()
    {
        TodoItem first = TestTodoFactory.Create("todo-1");
        TodoItem second = TestTodoFactory.Create("todo-2");
        StageLoad(first, second);

        BulkTodoResult result = await HandleAsync(
            TodoStatus.Completed,
            Select(first, second));

        result.Items.Select(item => item.Status).Should()
            .AllBeEquivalentTo(TodoStatus.Completed);
        result.Items.Select(item => item.Version).Should().AllBeEquivalentTo(2L);
        capturedUpdates.Should().HaveCount(2);
        capturedInserts.Should().BeEmpty();
        await repository.Received(1).SaveBatchAsync(
            Arg.Any<IReadOnlyCollection<TodoItem>>(),
            Arg.Any<IReadOnlyCollection<TodoItem>>(),
            Arg.Any<CancellationToken>());
        transactionExecutor.ExecutionCount.Should().Be(1);
    }

    [TestMethod]
    public async Task PrerequisiteAndDependentCompleteTogether()
    {
        TodoItem prerequisite = TestTodoFactory.Create("prerequisite");
        TodoItem dependent = TestTodoFactory.Create("dependent");
        dependent.AddDependency(prerequisite.Id, TestTodoFactory.Timestamp);
        StageLoad(prerequisite, dependent);

        BulkTodoResult result = await HandleAsync(
            TodoStatus.Completed,
            Select(prerequisite, dependent));

        result.Items.Select(item => item.Status).Should()
            .AllBeEquivalentTo(TodoStatus.Completed);
        capturedUpdates.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task DependencyOutsideTheBatchBlocksTheWholeBatch()
    {
        TodoItem prerequisite = TestTodoFactory.Create("prerequisite");
        TodoItem dependent = TestTodoFactory.Create("dependent");
        dependent.AddDependency(prerequisite.Id, TestTodoFactory.Timestamp);
        StageLoad(dependent);
        StageDependencies(prerequisite);

        Func<Task> act = () => HandleAsync(TodoStatus.Completed, Select(dependent));

        await act.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("A blocked TODO cannot move to Completed.");
        await AssertNoWriteAsync();
    }

    [TestMethod]
    public async Task ArchivedTodoInACompleteBatchRejectsTheWholeBatch()
    {
        TodoItem archived = TestTodoFactory.Create("todo-1");
        _ = archived.ChangeStatus(TodoStatus.Archived, TestTodoFactory.Timestamp);
        TodoItem active = TestTodoFactory.Create("todo-2");
        StageLoad(archived, active);

        Func<Task> act = () => HandleAsync(
            TodoStatus.Completed,
            Select(archived, active));

        await act.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("An archived TODO must be unarchived before it can be completed.");
        await AssertNoWriteAsync();
    }

    [TestMethod]
    public async Task ItemsAlreadyAtTheTargetStatusKeepTheirVersionAndAreNotWritten()
    {
        TodoItem completed = TestTodoFactory.Create("todo-1");
        _ = completed.ChangeStatus(TodoStatus.Completed, TestTodoFactory.Timestamp);
        completed.ClearDomainEvents();
        TodoItem pending = TestTodoFactory.Create("todo-2");
        StageLoad(completed, pending);

        BulkTodoResult result = await HandleAsync(
            TodoStatus.Completed,
            Select(completed, pending));

        result.Items.Single(item => item.Id == completed.Id).Version.Should().Be(1);
        result.Items.Single(item => item.Id == pending.Id).Version.Should().Be(2);
        capturedUpdates.Should().ContainSingle()
            .Which.Id.Should().Be(pending.Id);
    }

    [TestMethod]
    public async Task ABatchThatChangesNothingWritesNothing()
    {
        TodoItem archived = TestTodoFactory.Create("todo-1");
        _ = archived.ChangeStatus(TodoStatus.Archived, TestTodoFactory.Timestamp);
        StageLoad(archived);

        BulkTodoResult result = await HandleAsync(TodoStatus.Archived, Select(archived));

        result.Items.Should().ContainSingle()
            .Which.Version.Should().Be(1);
        await AssertNoWriteAsync();
        transactionExecutor.ExecutionCount.Should().Be(0);
    }

    [TestMethod]
    public async Task RecurringCompletionsCreateOneOccurrenceEach()
    {
        TodoItem first = CreateRecurring("todo-1", "series-1");
        TodoItem second = CreateRecurring("todo-2", "series-2");
        StageLoad(first, second);

        BulkTodoResult result = await HandleAsync(
            TodoStatus.Completed,
            Select(first, second));

        capturedInserts.Should().HaveCount(2);
        capturedInserts!.Select(insert => insert.OccurrenceNumber).Should()
            .AllBeEquivalentTo(2);
        result.Items.Should().OnlyContain(item => item.NextOccurrenceId != null);
        result.Items.Select(item => item.NextOccurrenceId).Should().OnlyHaveUniqueItems();
        await repository.Received(1).SaveBatchAsync(
            Arg.Any<IReadOnlyCollection<TodoItem>>(),
            Arg.Any<IReadOnlyCollection<TodoItem>>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ArchivingIgnoresDependencyState()
    {
        TodoItem prerequisite = TestTodoFactory.Create("prerequisite");
        TodoItem dependent = TestTodoFactory.Create("dependent");
        dependent.AddDependency(prerequisite.Id, TestTodoFactory.Timestamp);
        StageLoad(dependent);

        BulkTodoResult result = await HandleAsync(TodoStatus.Archived, Select(dependent));

        result.Items.Should().ContainSingle()
            .Which.Status.Should().Be(TodoStatus.Archived);
        await repository.DidNotReceive().GetByIdsAsync(
            Arg.Any<IEnumerable<Guid>>(),
            true,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Batch membership satisfies a dependency only when the batch completes it.
    /// A batch moving to <see cref="TodoStatus.InProgress"/> leaves the
    /// prerequisite short of completion, so it must block rather than reuse the
    /// exemption that lets a prerequisite and dependent complete together.
    /// </summary>
    [TestMethod]
    public async Task PrerequisiteInsideTheBatchStillBlocksAMoveToInProgress()
    {
        TodoItem prerequisite = TestTodoFactory.Create("prerequisite");
        TodoItem dependent = TestTodoFactory.Create("dependent");
        dependent.AddDependency(prerequisite.Id, TestTodoFactory.Timestamp);
        StageLoad(prerequisite, dependent);

        Func<Task> act = () => HandleAsync(
            TodoStatus.InProgress,
            Select(prerequisite, dependent));

        await act.Should()
            .ThrowAsync<DomainException>()
            .WithMessage("A blocked TODO cannot move to InProgress.");
        await AssertNoWriteAsync();
    }

    [TestMethod]
    public async Task MoveToInProgressAcceptsACompletedDependencyOutsideTheBatch()
    {
        TodoItem prerequisite = TestTodoFactory.Create("prerequisite");
        _ = prerequisite.ChangeStatus(TodoStatus.Completed, TestTodoFactory.Timestamp);
        TodoItem dependent = TestTodoFactory.Create("dependent");
        dependent.AddDependency(prerequisite.Id, TestTodoFactory.Timestamp);
        StageLoad(dependent);
        StageDependencies(prerequisite);

        BulkTodoResult result = await HandleAsync(
            TodoStatus.InProgress,
            Select(dependent));

        result.Items.Should().ContainSingle()
            .Which.Status.Should().Be(TodoStatus.InProgress);
        capturedUpdates.Should().ContainSingle();
    }

    [TestMethod]
    public async Task UnarchivingIgnoresDependencyState()
    {
        TodoItem prerequisite = TestTodoFactory.Create("prerequisite");
        TodoItem archived = TestTodoFactory.Create("archived");
        archived.AddDependency(prerequisite.Id, TestTodoFactory.Timestamp);
        _ = archived.ChangeStatus(TodoStatus.Archived, TestTodoFactory.Timestamp);
        StageLoad(archived);

        BulkTodoResult result = await HandleAsync(
            TodoStatus.Open,
            Select(archived));

        result.Items.Should().ContainSingle()
            .Which.Status.Should().Be(TodoStatus.Open);
        await repository.DidNotReceive().GetByIdsAsync(
            Arg.Any<IEnumerable<Guid>>(),
            true,
            Arg.Any<CancellationToken>());
    }

    private static TodoItem CreateRecurring(string id, string seriesId)
    {
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            TestTodoFactory.DueDate);

        return TodoItem.Create(
            TestTodoFactory.CreateId(id),
            TestTodoFactory.OwnerId,
            "Submit report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.High,
            TestTodoFactory.Timestamp,
            recurrence,
            TestTodoFactory.CreateId(seriesId),
            1);
    }

    private static BulkTodoItemRequest[] Select(params TodoItem[] todos)
    {
        return todos
            .Select(todo => new BulkTodoItemRequest(todo.Id, todo.Version))
            .ToArray();
    }

    private async Task AssertNoWriteAsync()
    {
        await repository.DidNotReceiveWithAnyArgs().SaveBatchAsync(
            default!,
            default!,
            default);
    }

    private void StageLoad(params TodoItem[] todos)
    {
        repository.GetByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(todos);
    }

    private void StageDependencies(params TodoItem[] dependencies)
    {
        repository.GetByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns(dependencies);
    }

    private Task<BulkTodoResult> HandleAsync(
        TodoStatus status,
        BulkTodoItemRequest[] selection,
        params BulkTodoItemRequest[] extra)
    {
        BulkTodoItemRequest[] items = selection.Concat(extra).ToArray();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(TestTodoFactory.Timestamp.AddHours(1));
        BulkChangeTodoStatusCommandHandler handler = new BulkChangeTodoStatusCommandHandler(
            repository,
            new RecurringOccurrenceFactory(new RecurrenceCalculator()),
            clock,
            transactionExecutor,
            NullLogger<BulkChangeTodoStatusCommandHandler>.Instance);

        return handler.Handle(
            new BulkChangeTodoStatusCommand(status, items),
            CancellationToken.None);
    }
}
