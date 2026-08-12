using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Sleeky.Todo.Application.Abstractions.Events;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Abstractions.Time;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Dependencies;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Domain.Events;
using Sleeky.Todo.Domain.Exceptions;
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
            new ImmediateTodoTransaction(),
            new IgnoringDomainEventDispatcher(),
            Substitute.For<ILogger<ChangeTodoStatusCommandHandler>>());

        Func<Task> act = async () => await handler.Handle(
            new ChangeTodoStatusCommand(todo.Id, status, 1),
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<DomainException>()
            .WithMessage($"A blocked TODO cannot move to {status}.");
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(
            default!,
            default,
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
        repository.UpdateAsync(todo, 1, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(todo, 2));
        ChangeTodoStatusCommandHandler handler = new ChangeTodoStatusCommandHandler(
            repository,
            evaluator,
            clock,
            new ImmediateTodoTransaction(),
            new IgnoringDomainEventDispatcher(),
            Substitute.For<ILogger<ChangeTodoStatusCommandHandler>>());

        TodoDto result = await handler.Handle(
            new ChangeTodoStatusCommand(todo.Id, TodoStatus.Completed, 1),
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
            new ImmediateTodoTransaction(),
            new IgnoringDomainEventDispatcher(),
            Substitute.For<ILogger<ChangeTodoStatusCommandHandler>>());

        TodoDto unchanged = await handler.Handle(
            new ChangeTodoStatusCommand(todo.Id, TodoStatus.NotStarted, 1),
            CancellationToken.None);
        Func<Task> stale = async () => await handler.Handle(
            new ChangeTodoStatusCommand(todo.Id, TodoStatus.NotStarted, 2),
            CancellationToken.None);

        unchanged.Version.Should().Be(1);
        await stale.Should().ThrowAsync<ConcurrencyConflictException>();
        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(
            default!,
            default,
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
        CompletingTodoTransaction transaction = new CompletingTodoTransaction();
        RecordingLogger logger = new RecordingLogger(() => transaction.Completed);
        clock.UtcNow.Returns(TestTodoFactory.Timestamp.AddDays(1));
        repository.GetByIdAsync(todo.Id, false, Arg.Any<CancellationToken>())
            .Returns(todo);
        evaluator.EvaluateAsync(
                Arg.Any<IEnumerable<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new TodoDependencyState(0));
        repository.UpdateAsync(todo, 1, Arg.Any<CancellationToken>())
            .Returns(_ => TestTodoFactory.WithVersion(todo, 2));
        ChangeTodoStatusCommandHandler handler = new ChangeTodoStatusCommandHandler(
            repository,
            evaluator,
            clock,
            transaction,
            new IgnoringDomainEventDispatcher(),
            logger);

        TodoDto result = await handler.Handle(
            new ChangeTodoStatusCommand(todo.Id, TodoStatus.Completed, 1),
            CancellationToken.None);

        transaction.Completed.Should().BeTrue();
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

    private static TodoItem CreateTodoWithDependency()
    {
        TodoItem todo = TestTodoFactory.Create();
        todo.AddDependency(TestTodoFactory.CreateId("dependency"), TestTodoFactory.Timestamp);
        return todo;
    }

    private sealed class ImmediateTodoTransaction : ITodoTransaction
    {
        public Task<TResult> ExecuteAsync<TResult>(
            Guid todoId,
            long expectedVersion,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            return operation(cancellationToken);
        }
    }

    private sealed class IgnoringDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(
            IEnumerable<IDomainEvent> domainEvents,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CompletingTodoTransaction : ITodoTransaction
    {
        public bool Completed { get; private set; }

        public async Task<TResult> ExecuteAsync<TResult>(
            Guid todoId,
            long expectedVersion,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            TResult result = await operation(cancellationToken);
            Completed = true;
            return result;
        }
    }

    private sealed class RecordingLogger : ILogger<ChangeTodoStatusCommandHandler>
    {
        private readonly Func<bool> isTransactionCompleted;

        public RecordingLogger(Func<bool> isTransactionCompleted)
        {
            this.isTransactionCompleted = isTransactionCompleted;
        }

        public List<LogEntry> Entries { get; } = new List<LogEntry>();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Dictionary<string, object?> properties = state
                is IEnumerable<KeyValuePair<string, object?>> structuredState
                    ? structuredState.ToDictionary(pair => pair.Key, pair => pair.Value)
                    : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(
                logLevel,
                eventId.Id,
                properties,
                isTransactionCompleted()));
        }
    }

    private sealed class LogEntry
    {
        public LogEntry(
            LogLevel level,
            int eventId,
            IReadOnlyDictionary<string, object?> properties,
            bool transactionCompleted)
        {
            Level = level;
            EventId = eventId;
            Properties = properties;
            TransactionCompleted = transactionCompleted;
        }

        public int EventId { get; }

        public LogLevel Level { get; }

        public IReadOnlyDictionary<string, object?> Properties { get; }

        public bool TransactionCompleted { get; }
    }
}
