using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Application.Todos.Recurrence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.Services;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.ChangeTodoStatus;

[TestClass]
public sealed class ChangeTodoStatusCommandHandlerTests
{
    [TestMethod]
    [DataRow(TodoStatus.InProgress)]
    [DataRow(TodoStatus.Completed)]
    public async Task BlockedTodoCannotEnterActiveOrCompletedStatus(TodoStatus status)
    {
        TodoItem todo = CreateTodoWithDependency();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        ITodoDependencyEvaluator evaluator = Substitute.For<ITodoDependencyEvaluator>();
        IClock clock = Substitute.For<IClock>();
        repository.GetByIdAsync(todo.Id, false, Arg.Any<CancellationToken>())
            .Returns(todo);
        evaluator.EvaluateAsync(
                Arg.Any<IEnumerable<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new TodoDependencyState(1));
        ChangeTodoStatusCommandHandler handler = new ChangeTodoStatusCommandHandler(
            repository,
            evaluator,
            clock,
            new ImmediateTransactionExecutor(),
            new RecurringOccurrenceFactory(new RecurrenceCalculator()),
            Substitute.For<ILogger<ChangeTodoStatusCommandHandler>>());

        Func<Task> act = async () => await handler.Handle(
            new ChangeTodoStatusCommand(TestTodoFactory.SpaceId, todo.Id, status, 1),
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<DomainException>()
            .WithMessage($"A blocked TODO cannot move to {status}.");
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(
            default!,
            default);
    }

    [TestMethod]
    public async Task TodoCanTransitionAfterDependenciesAreCompleted()
    {
        TodoItem todo = CreateTodoWithDependency();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        ITodoDependencyEvaluator evaluator = Substitute.For<ITodoDependencyEvaluator>();
        IClock clock = Substitute.For<IClock>();
        DateTimeOffset updatedAt = TestTodoFactory.Timestamp.AddHours(1);
        clock.UtcNow.Returns(updatedAt);
        repository.GetByIdAsync(todo.Id, false, Arg.Any<CancellationToken>())
            .Returns(todo);
        evaluator.EvaluateAsync(
                Arg.Any<IEnumerable<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new TodoDependencyState(0));
        repository.UpdateAsync(todo, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(todo, 2));
        ChangeTodoStatusCommandHandler handler = new ChangeTodoStatusCommandHandler(
            repository,
            evaluator,
            clock,
            new ImmediateTransactionExecutor(),
            new RecurringOccurrenceFactory(new RecurrenceCalculator()),
            Substitute.For<ILogger<ChangeTodoStatusCommandHandler>>());

        TodoDto result = await handler.Handle(
            new ChangeTodoStatusCommand(TestTodoFactory.SpaceId, todo.Id, TodoStatus.Completed, 1),
            CancellationToken.None);

        result.Status.Should().Be(TodoStatus.Completed);
        result.Version.Should().Be(2);
        result.UpdatedAt.Should().Be(updatedAt);
    }

    [TestMethod]
    public async Task SameStatusIsNoOpButStaleVersionStillConflicts()
    {
        TodoItem todo = TestTodoFactory.Create();
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        ITodoDependencyEvaluator evaluator = Substitute.For<ITodoDependencyEvaluator>();
        IClock clock = Substitute.For<IClock>();
        repository.GetByIdAsync(todo.Id, false, Arg.Any<CancellationToken>())
            .Returns(todo);
        ChangeTodoStatusCommandHandler handler = new ChangeTodoStatusCommandHandler(
            repository,
            evaluator,
            clock,
            new ImmediateTransactionExecutor(),
            new RecurringOccurrenceFactory(new RecurrenceCalculator()),
            Substitute.For<ILogger<ChangeTodoStatusCommandHandler>>());

        TodoDto unchanged = await handler.Handle(
            new ChangeTodoStatusCommand(TestTodoFactory.SpaceId, todo.Id, TodoStatus.Open, 1),
            CancellationToken.None);
        Func<Task> stale = async () => await handler.Handle(
            new ChangeTodoStatusCommand(TestTodoFactory.SpaceId, todo.Id, TodoStatus.Open, 2),
            CancellationToken.None);

        unchanged.Version.Should().Be(1);
        await stale.Should().ThrowAsync<ConcurrencyConflictException>();
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(
            default!,
            default);
    }

    [TestMethod]
    public async Task RecurringCompletionLogsCreatedTodoAfterTransactionCompletes()
    {
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            TestTodoFactory.DueDate);
        TodoItem todo = TodoItem.Create(
            TestTodoFactory.CreateId("todo-1"),
            TestTodoFactory.SpaceId,
            TestTodoFactory.CreatedByUserId,
            "Submit report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.High,
            TestTodoFactory.Timestamp,
            recurrence,
            TestTodoFactory.CreateId("series-1"),
            1);
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        ITodoDependencyEvaluator evaluator = Substitute.For<ITodoDependencyEvaluator>();
        IClock clock = Substitute.For<IClock>();
        CompletingTransactionExecutor transactionExecutor = new CompletingTransactionExecutor();
        RecordingLogger logger = new RecordingLogger(() => transactionExecutor.Completed);
        clock.UtcNow.Returns(TestTodoFactory.Timestamp.AddDays(1));
        repository.GetByIdAsync(todo.Id, false, Arg.Any<CancellationToken>())
            .Returns(todo);
        evaluator.EvaluateAsync(
                Arg.Any<IEnumerable<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new TodoDependencyState(0));
        repository.UpdateAsync(todo, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(todo, 2));
        ChangeTodoStatusCommandHandler handler = new ChangeTodoStatusCommandHandler(
            repository,
            evaluator,
            clock,
            transactionExecutor,
            new RecurringOccurrenceFactory(new RecurrenceCalculator()),
            logger);

        TodoDto result = await handler.Handle(
            new ChangeTodoStatusCommand(TestTodoFactory.SpaceId, todo.Id, TodoStatus.Completed, 1),
            CancellationToken.None);

        transactionExecutor.Completed.Should().BeTrue();
        LogEntry entry = logger.Entries.Should()
            .ContainSingle(candidate => candidate.EventId == 1101)
            .Which;
        entry.TransactionCompleted.Should().BeTrue();
        entry.Level.Should().Be(LogLevel.Information);
        entry.Properties["TodoId"].Should().Be(result.NextOccurrenceId);
        entry.Properties["SeriesId"].Should().Be(TestTodoFactory.CreateId("series-1"));
        entry.Properties["CompletedTodoId"].Should().Be(TestTodoFactory.CreateId("todo-1"));
        logger.Entries.Should().ContainSingle(candidate => candidate.EventId == 1108);
    }

    /// <summary>
    /// A reopened occurrence completed again already has its successor from
    /// the first completion. It is completed with a single write and reports
    /// no new occurrence, rather than colliding with the unique series index.
    /// </summary>
    [TestMethod]
    public async Task ARecurringCompletionWhoseSuccessorExistsWritesOnlyTheCompletion()
    {
        RecurrenceSchedule recurrence = RecurrenceSchedule.Create(
            RecurrenceType.Monthly,
            1,
            null,
            TestTodoFactory.DueDate);
        Guid seriesId = TestTodoFactory.CreateId("series-1");
        TodoItem todo = TodoItem.Create(
            TestTodoFactory.CreateId("todo-1"),
            TestTodoFactory.SpaceId,
            TestTodoFactory.CreatedByUserId,
            "Submit report",
            null,
            TestTodoFactory.DueDate,
            TodoPriority.High,
            TestTodoFactory.Timestamp,
            recurrence,
            seriesId,
            1);
        ITodoRepository repository = Substitute.For<ITodoRepository>();
        ITodoDependencyEvaluator evaluator = Substitute.For<ITodoDependencyEvaluator>();
        IClock clock = Substitute.For<IClock>();
        CompletingTransactionExecutor transactionExecutor = new CompletingTransactionExecutor();
        RecordingLogger logger = new RecordingLogger(() => transactionExecutor.Completed);
        clock.UtcNow.Returns(TestTodoFactory.Timestamp.AddDays(1));
        repository.GetByIdAsync(todo.Id, false, Arg.Any<CancellationToken>())
            .Returns(todo);
        repository.GetExistingSeriesOccurrencesAsync(
                Arg.Is<IReadOnlyCollection<TodoSeriesOccurrence>>(occurrences =>
                    occurrences.Single() == new TodoSeriesOccurrence(seriesId, 2)),
                Arg.Any<CancellationToken>())
            .Returns([new TodoSeriesOccurrence(seriesId, 2)]);
        evaluator.EvaluateAsync(
                Arg.Any<IEnumerable<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new TodoDependencyState(0));
        repository.UpdateAsync(todo, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(todo, 2));
        ChangeTodoStatusCommandHandler handler = new ChangeTodoStatusCommandHandler(
            repository,
            evaluator,
            clock,
            transactionExecutor,
            new RecurringOccurrenceFactory(new RecurrenceCalculator()),
            logger);

        TodoDto result = await handler.Handle(
            new ChangeTodoStatusCommand(TestTodoFactory.SpaceId, todo.Id, TodoStatus.Completed, 1),
            CancellationToken.None);

        result.Status.Should().Be(TodoStatus.Completed);
        result.NextOccurrenceId.Should().BeNull();
        transactionExecutor.Completed.Should().BeFalse();
        await repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        logger.Entries.Should().NotContain(candidate => candidate.EventId == 1101);
        logger.Entries.Should().ContainSingle(candidate => candidate.EventId == 1113);
    }

    private static TodoItem CreateTodoWithDependency()
    {
        TodoItem todo = TestTodoFactory.Create();
        todo.AddDependency(TestTodoFactory.CreateId("dependency"), TestTodoFactory.Timestamp);
        return todo;
    }
}
