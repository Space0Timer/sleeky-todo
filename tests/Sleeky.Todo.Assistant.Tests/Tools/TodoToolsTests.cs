using FluentAssertions;

using MediatR;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;
using Sleeky.Todo.Assistant.Conflicts;
using Sleeky.Todo.Assistant.Tests.Turns;
using Sleeky.Todo.Assistant.Tools;
using Sleeky.Todo.Assistant.Turns;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Assistant.Tests.Tools;

[TestClass]
public sealed class TodoToolsTests
{
    private static readonly Guid First = TestTodo.Id("todo-1");

    private static readonly Guid Second = TestTodo.Id("todo-2");

    /// <summary>
    /// The agent-side analogue of the delete dialog. A proposal asks and stops;
    /// it does not delete and then mention it.
    /// </summary>
    [TestMethod]
    public async Task DeleteProposesAndHaltsWithoutDispatchingADeletion()
    {
        Harness harness = new Harness();
        harness.StageSelection(TestTodo.At(First, 5, "Submit report"));

        object outcome = await harness.Tools.DeleteTodosAsync(
            new[] { First.ToString() },
            CancellationToken.None);

        outcome.Should().BeOfType<ToolFailure>();
        harness.Halted.Should().BeTrue();
        await harness.Policy.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);

        ConfirmationRequest? request =
            harness.Events.Single<ConfirmationRequest>(TurnEventType.ConfirmationRequired);
        request.Should().NotBeNull();
        request!.Tool.Should().Be(TodoToolNames.DeleteTodos);
        request.Items.Single().Version.Should().Be(5);
        request.Items.Single().Name.Should().Be("Submit report");
    }

    /// <summary>
    /// What the user was shown is what gets written, so the window between
    /// deciding and acting is the confirmation itself rather than whatever the
    /// store holds when the answer arrives.
    /// </summary>
    [TestMethod]
    public async Task ConfirmedDeletionSendsTheVersionsThatWereDisplayed()
    {
        Harness harness = new Harness();
        harness.Policy
            .DeleteAsync(Arg.Any<IReadOnlyCollection<BulkTodoItemRequest>>(), Arg.Any<CancellationToken>())
            .Returns(Applied(First, 6, TodoStatus.NotStarted, deleted: true));

        await harness.Tools.ExecuteConfirmedDeletionAsync(
            new ConfirmedAction(
                TodoToolNames.DeleteTodos,
                new[] { new TodoVersionReference(First, 5) }),
            CancellationToken.None);

        await harness.Policy.Received(1).DeleteAsync(
            Arg.Is<IReadOnlyCollection<BulkTodoItemRequest>>(items =>
                items.Single().Id == First && items.Single().Version == 5),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Replay safety comes free from the version binding: the second confirm
    /// carries a version the store has moved past.
    /// </summary>
    [TestMethod]
    public async Task ReplayedConfirmationFailsOnTheMovedVersion()
    {
        Harness harness = new Harness();
        harness.Policy
            .DeleteAsync(Arg.Any<IReadOnlyCollection<BulkTodoItemRequest>>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(Applied(First, 6, TodoStatus.NotStarted, deleted: true)),
                _ => throw new BulkConcurrencyConflictException("TODO", new[] { First }));
        ConfirmedAction confirmation = new ConfirmedAction(
            TodoToolNames.DeleteTodos,
            new[] { new TodoVersionReference(First, 5) });

        await harness.Tools.ExecuteConfirmedDeletionAsync(confirmation, CancellationToken.None);
        Func<Task> replay = async () =>
            await harness.Tools.ExecuteConfirmedDeletionAsync(confirmation, CancellationToken.None);

        await replay.Should().ThrowAsync<BulkConcurrencyConflictException>();
    }

    [TestMethod]
    public async Task ChangeStatusRefusesToWriteAgainstSomethingNeverRead()
    {
        Harness harness = new Harness();

        object outcome = await harness.Tools.ChangeTodoStatusAsync(
            "Completed",
            new[] { First.ToString() },
            CancellationToken.None);

        outcome.Should().BeOfType<ToolFailure>()
            .Which.Error.Should().Contain("Read them first");
        await harness.Policy.DidNotReceiveWithAnyArgs().ChangeStatusAsync(default, default!, default);
    }

    [TestMethod]
    public async Task ChangeStatusBindsTheVersionsTheModelLastRead()
    {
        Harness harness = new Harness();
        harness.Ledger.Record(First, 5);
        harness.Ledger.Record(Second, 9);
        harness.Policy
            .ChangeStatusAsync(Arg.Any<TodoStatus>(), Arg.Any<IReadOnlyCollection<BulkTodoItemRequest>>(), Arg.Any<CancellationToken>())
            .Returns(Applied(First, 6, TodoStatus.Completed, deleted: false));

        await harness.Tools.ChangeTodoStatusAsync(
            "Completed",
            new[] { First.ToString(), Second.ToString() },
            CancellationToken.None);

        await harness.Policy.Received(1).ChangeStatusAsync(
            TodoStatus.Completed,
            Arg.Is<IReadOnlyCollection<BulkTodoItemRequest>>(items =>
                items.Count == 2
                && items.First().Version == 5
                && items.Last().Version == 9),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Refuse and narrow. Chunking would abandon the all-or-nothing guarantee
    /// and leave the assistant unable to describe what actually happened.
    /// </summary>
    [TestMethod]
    public async Task WritesRefuseAnOverCapBatchRatherThanSplittingIt()
    {
        Harness harness = new Harness();
        string[] tooMany = Enumerable
            .Range(0, BulkTodoLimits.MaximumSelectionSize + 1)
            .Select(index => TestTodo.Id($"todo-{index}").ToString())
            .ToArray();

        object outcome = await harness.Tools.ChangeTodoStatusAsync(
            "Completed",
            tooMany,
            CancellationToken.None);

        outcome.Should().BeOfType<ToolFailure>()
            .Which.Error.Should()
            .Contain(BulkTodoLimits.MaximumSelectionSize.ToString())
            .And.Contain("Narrow the selection");
        await harness.Policy.DidNotReceiveWithAnyArgs().ChangeStatusAsync(default, default!, default);
    }

    [TestMethod]
    public async Task ReadsRecordVersionsSoALaterWriteCanBind()
    {
        Harness harness = new Harness();
        harness.StageSelection(TestTodo.At(First, 12));

        object read = await harness.Tools.GetTodoSelectionAsync(
            new[] { First.ToString() },
            CancellationToken.None);

        read.Should().BeOfType<TodoPage>()
            .Which.Items.Single().Version.Should().Be(12);
        harness.Ledger
            .TryBind(new[] { First }, out IReadOnlyCollection<BulkTodoItemRequest> bound, out _)
            .Should().BeTrue();
        bound.Single().Version.Should().Be(12);
    }

    [TestMethod]
    public async Task AWriteReportsWhatChangedAndWhatWasAlreadySatisfied()
    {
        Harness harness = new Harness();
        harness.Ledger.Record(First, 5);
        harness.Ledger.Record(Second, 9);
        harness.Policy
            .ChangeStatusAsync(Arg.Any<TodoStatus>(), Arg.Any<IReadOnlyCollection<BulkTodoItemRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new BulkTodoResult(new[]
            {
                new BulkTodoResultItem(First, 6, TodoStatus.Completed, null, null),
                new BulkTodoResultItem(Second, 9, TodoStatus.Completed, null, null),
            }));

        object outcome = await harness.Tools.ChangeTodoStatusAsync(
            "Completed",
            new[] { First.ToString(), Second.ToString() },
            CancellationToken.None);

        TodoWriteOutcome written = outcome.Should().BeOfType<TodoWriteOutcome>().Subject;
        written.Changed.Should().Be(1);
        written.Unchanged.Should().Be(1);
        harness.Events.Types().Should()
            .Contain(TurnEventType.ToolExecuted)
            .And.Contain(TurnEventType.TodosChanged);
    }

    /// <summary>
    /// A confirmed selection comes from a client rather than through a tool
    /// schema, and it runs outside the loop — so a validation failure would
    /// stop the stream mid-turn rather than reach anything that can explain it.
    /// </summary>
    [TestMethod]
    public async Task ConfirmedDeletionRefusesASelectionTheCommandWouldReject()
    {
        Harness harness = new Harness();

        object empty = await harness.Tools.ExecuteConfirmedDeletionAsync(
            new ConfirmedAction(TodoToolNames.DeleteTodos, Array.Empty<TodoVersionReference>()),
            CancellationToken.None);

        object duplicated = await harness.Tools.ExecuteConfirmedDeletionAsync(
            new ConfirmedAction(
                TodoToolNames.DeleteTodos,
                new[] { new TodoVersionReference(First, 5), new TodoVersionReference(First, 5) }),
            CancellationToken.None);

        empty.Should().BeOfType<ToolFailure>();
        duplicated.Should().BeOfType<ToolFailure>();
        await harness.Policy.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [TestMethod]
    public async Task ConfirmedDeletionRefusesAnOverCapSelection()
    {
        Harness harness = new Harness();
        TodoVersionReference[] tooMany = Enumerable
            .Range(0, BulkTodoLimits.MaximumSelectionSize + 1)
            .Select(index => new TodoVersionReference(TestTodo.Id($"todo-{index}"), 1))
            .ToArray();

        object outcome = await harness.Tools.ExecuteConfirmedDeletionAsync(
            new ConfirmedAction(TodoToolNames.DeleteTodos, tooMany),
            CancellationToken.None);

        outcome.Should().BeOfType<ToolFailure>();
        await harness.Policy.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [TestMethod]
    public async Task GetTodosRefusesAnOutOfRangeLimitRatherThanThrowing()
    {
        Harness harness = new Harness();

        object outcome = await harness.Tools.GetTodosAsync(
            null,
            null,
            null,
            null,
            null,
            limit: 500,
            CancellationToken.None);

        outcome.Should().BeOfType<ToolFailure>()
            .Which.Error.Should().Contain("limit");
    }

    [TestMethod]
    public async Task AMalformedIdentifierComesBackAsSomethingTheModelCanFix()
    {
        Harness harness = new Harness();

        object outcome = await harness.Tools.GetTodoSelectionAsync(
            new[] { "not-an-identifier" },
            CancellationToken.None);

        outcome.Should().BeOfType<ToolFailure>()
            .Which.Error.Should().Contain("not a TODO identifier");
    }

    [TestMethod]
    public async Task AnUnknownStatusNamesTheOnesThatWork()
    {
        Harness harness = new Harness();

        object outcome = await harness.Tools.ChangeTodoStatusAsync(
            "Finished",
            new[] { First.ToString() },
            CancellationToken.None);

        outcome.Should().BeOfType<ToolFailure>()
            .Which.Error.Should().Contain("Completed");
    }

    private static BulkTodoResult Applied(
        Guid id,
        long version,
        TodoStatus status,
        bool deleted)
    {
        return new BulkTodoResult(new[]
        {
            new BulkTodoResultItem(
                id,
                version,
                status,
                deleted ? TestTodo.Timestamp : null,
                NextOccurrenceId: null),
        });
    }

    private sealed class Harness
    {
        public Harness()
        {
            this.Sender = Substitute.For<ISender>();
            this.Policy = Substitute.For<IBulkConflictPolicy>();
            this.Ledger = new TodoVersionLedger();
            this.Events = new RecordingTurnEvents();
            this.Tools = new TodoTools(
                this.Sender,
                this.Policy,
                this.Ledger,
                this.Events,
                new StubTurnController(this),
                NullLogger<TodoTools>.Instance);
        }

        public ISender Sender { get; }

        public IBulkConflictPolicy Policy { get; }

        public TodoVersionLedger Ledger { get; }

        public RecordingTurnEvents Events { get; }

        public TodoTools Tools { get; }

        public bool Halted { get; private set; }

        public void StageSelection(params TodoDto[] found)
        {
            this.Sender
                .Send(Arg.Any<IRequest<TodoSelection>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new TodoSelection(found)));
        }

        private sealed class StubTurnController : ITurnController
        {
            private readonly Harness harness;

            public StubTurnController(Harness harness)
            {
                this.harness = harness;
            }

            public void Halt()
            {
                this.harness.Halted = true;
            }
        }
    }
}
