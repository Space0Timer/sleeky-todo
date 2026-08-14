using FluentAssertions;

using MediatR;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Commands.Bulk.ChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Commands.Bulk.DeleteTodos;
using Sleeky.Todo.Application.Todos.Commands.Bulk.RestoreTodos;
using Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;
using Sleeky.Todo.Assistant.Conflicts;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Assistant.Tests.Conflicts;

[TestClass]
public sealed class BulkConflictPolicyTests
{
    private static readonly Guid First = TestTodo.Id("todo-1");

    private static readonly Guid Second = TestTodo.Id("todo-2");

    /// <summary>
    /// The retry must send what the re-read found, not what the first attempt
    /// sent: resending the stale versions would fail identically forever.
    /// </summary>
    [TestMethod]
    public async Task ChangeStatusRetriesOnceWithTheVersionsItReRead()
    {
        List<BulkChangeTodoStatusCommand> attempts = new List<BulkChangeTodoStatusCommand>();
        ISender sender = Substitute.For<ISender>();
        StageStatusAttempts(sender, attempts, failFirst: true);
        StageSelection(sender, TestTodo.At(First, 7), TestTodo.At(Second, 4));

        BulkTodoResult result = await Policy(sender).ChangeStatusAsync(
            TodoStatus.Completed,
            Selection((First, 5), (Second, 3)),
            CancellationToken.None);

        attempts.Should().HaveCount(2);
        attempts[0].Items.Select(item => item.Version).Should().Equal(5L, 3L);
        attempts[1].Items.Select(item => item.Version).Should().Equal(7L, 4L);
        result.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// Retrying a shrunken selection would act on a subset the user never
    /// chose, so any absence goes back to the caller instead.
    /// </summary>
    [TestMethod]
    public async Task ChangeStatusAbandonsTheRetryWhenAnIdentifierNoLongerResolves()
    {
        List<BulkChangeTodoStatusCommand> attempts = new List<BulkChangeTodoStatusCommand>();
        ISender sender = Substitute.For<ISender>();
        StageStatusAttempts(sender, attempts, failFirst: true);
        StageSelection(sender, TestTodo.At(First, 7));

        Func<Task> act = async () => await Policy(sender).ChangeStatusAsync(
            TodoStatus.Completed,
            Selection((First, 5), (Second, 3)),
            CancellationToken.None);

        await act.Should().ThrowAsync<BulkConcurrencyConflictException>();
        attempts.Should().ContainSingle();
    }

    [TestMethod]
    public async Task ChangeStatusRetriesAtMostOnce()
    {
        List<BulkChangeTodoStatusCommand> attempts = new List<BulkChangeTodoStatusCommand>();
        ISender sender = Substitute.For<ISender>();
        StageStatusAttempts(sender, attempts, failFirst: true, failSecond: true);
        StageSelection(sender, TestTodo.At(First, 7));

        Func<Task> act = async () => await Policy(sender).ChangeStatusAsync(
            TodoStatus.Completed,
            Selection((First, 5)),
            CancellationToken.None);

        await act.Should().ThrowAsync<BulkConcurrencyConflictException>();
        attempts.Should().HaveCount(2);
    }

    /// <summary>
    /// Deletion is the batch whose intent can invert while the world moves, so
    /// it always returns to a person.
    /// </summary>
    [TestMethod]
    public async Task DeleteNeverRetries()
    {
        int attempts = 0;
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<IRequest<BulkTodoResult>>(), Arg.Any<CancellationToken>())
            .Returns<Task<BulkTodoResult>>(_ =>
            {
                attempts++;
                throw new BulkConcurrencyConflictException("TODO", new[] { First });
            });

        Func<Task> act = async () => await Policy(sender).DeleteAsync(
            Selection((First, 5)),
            CancellationToken.None);

        await act.Should().ThrowAsync<BulkConcurrencyConflictException>();
        attempts.Should().Be(1);
        await sender.DidNotReceive().Send(
            Arg.Any<IRequest<TodoSelection>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A conflicted restore means someone has already restored it, and the
    /// write asserts the stored document is still deleted, so a second attempt
    /// would fail anyway.
    /// </summary>
    [TestMethod]
    public async Task RestoreNeverRetries()
    {
        int attempts = 0;
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<IRequest<BulkTodoResult>>(), Arg.Any<CancellationToken>())
            .Returns<Task<BulkTodoResult>>(_ =>
            {
                attempts++;
                throw new BulkConcurrencyConflictException("TODO", new[] { First });
            });

        Func<Task> act = async () => await Policy(sender).RestoreAsync(
            Selection((First, 5)),
            CancellationToken.None);

        await act.Should().ThrowAsync<BulkConcurrencyConflictException>();
        attempts.Should().Be(1);
    }

    /// <summary>
    /// A domain rejection fails identically however often it is retried.
    /// </summary>
    [TestMethod]
    public async Task ChangeStatusDoesNotRetryADomainRejection()
    {
        int attempts = 0;
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<IRequest<BulkTodoResult>>(), Arg.Any<CancellationToken>())
            .Returns<Task<BulkTodoResult>>(_ =>
            {
                attempts++;
                throw new DomainRuleException(
                    "Cannot complete a blocked TODO.",
                    new InvalidOperationException());
            });

        Func<Task> act = async () => await Policy(sender).ChangeStatusAsync(
            TodoStatus.Completed,
            Selection((First, 5)),
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<DomainRuleException>()
            .WithMessage("Cannot complete a blocked TODO.");
        attempts.Should().Be(1);
    }

    [TestMethod]
    public async Task DeleteDispatchesTheDeletionCommand()
    {
        ISender sender = Substitute.For<ISender>();
        BulkDeleteTodosCommand? dispatched = null;
        sender.Send(Arg.Any<IRequest<BulkTodoResult>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                dispatched = call.Arg<IRequest<BulkTodoResult>>() as BulkDeleteTodosCommand;
                return Task.FromResult(Applied(First, 6));
            });

        await Policy(sender).DeleteAsync(Selection((First, 5)), CancellationToken.None);

        dispatched.Should().NotBeNull();
        dispatched!.Items.Single().Version.Should().Be(5);
    }

    [TestMethod]
    public async Task RestoreDispatchesTheRestoreCommand()
    {
        ISender sender = Substitute.For<ISender>();
        BulkRestoreTodosCommand? dispatched = null;
        sender.Send(Arg.Any<IRequest<BulkTodoResult>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                dispatched = call.Arg<IRequest<BulkTodoResult>>() as BulkRestoreTodosCommand;
                return Task.FromResult(Applied(First, 6));
            });

        await Policy(sender).RestoreAsync(Selection((First, 5)), CancellationToken.None);

        dispatched.Should().NotBeNull();
        dispatched!.Items.Single().Id.Should().Be(First);
    }

    private static BulkConflictPolicy Policy(ISender sender)
    {
        return new BulkConflictPolicy(sender, NullLogger<BulkConflictPolicy>.Instance);
    }

    private static BulkTodoItemRequest[] Selection(params (Guid Id, long Version)[] items)
    {
        return items.Select(item => new BulkTodoItemRequest(item.Id, item.Version)).ToArray();
    }

    private static BulkTodoResult Applied(params object[] pairs)
    {
        List<BulkTodoResultItem> items = new List<BulkTodoResultItem>();

        for (int index = 0; index < pairs.Length; index += 2)
        {
            items.Add(new BulkTodoResultItem(
                (Guid)pairs[index],
                (long)(int)pairs[index + 1],
                TodoStatus.Completed,
                DeletedAt: null,
                NextOccurrenceId: null));
        }

        return new BulkTodoResult(items);
    }

    private static void StageStatusAttempts(
        ISender sender,
        List<BulkChangeTodoStatusCommand> attempts,
        bool failFirst = false,
        bool failSecond = false)
    {
        sender.Send(Arg.Any<IRequest<BulkTodoResult>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                BulkChangeTodoStatusCommand command =
                    (BulkChangeTodoStatusCommand)call.Arg<IRequest<BulkTodoResult>>();
                attempts.Add(command);

                bool fails = (attempts.Count == 1 && failFirst)
                    || (attempts.Count == 2 && failSecond);

                if (fails)
                {
                    throw new BulkConcurrencyConflictException(
                        "TODO",
                        command.Items.Select(item => item.Id).ToArray());
                }

                return Task.FromResult(new BulkTodoResult(command.Items
                    .Select(item => new BulkTodoResultItem(
                        item.Id,
                        item.Version + 1,
                        command.Status,
                        DeletedAt: null,
                        NextOccurrenceId: null))
                    .ToArray()));
            });
    }

    private static void StageSelection(ISender sender, params TodoDto[] found)
    {
        sender.Send(Arg.Any<IRequest<TodoSelection>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TodoSelection(found)));
    }
}
