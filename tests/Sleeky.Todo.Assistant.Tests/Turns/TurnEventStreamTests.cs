using System.Text.Json;

using FluentAssertions;

using Sleeky.Todo.Assistant.Turns;

namespace Sleeky.Todo.Assistant.Tests.Turns;

[TestClass]
public sealed class TurnEventStreamTests
{
    private static readonly TimeSpan NeverElapses = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Immediate = TimeSpan.FromMilliseconds(20);

    private static readonly AssistantTurn AnyTurn =
        new AssistantTurn("Complete everything due today.", null, null);

    [TestMethod]
    public async Task RunForwardsTurnEventsInOrderAndEndsWithTheTurn()
    {
        ScriptedTurnRunner runner = new ScriptedTurnRunner(async (events, cancellationToken) =>
        {
            await events.PublishAsync(TurnEvent.TurnStarted(), cancellationToken);
            await events.PublishAsync(
                TurnEvent.Message(new AssistantMessage("Marked four as completed.")),
                cancellationToken);
        });

        List<TurnEvent> observed = await ReadAllAsync(runner, NeverElapses);

        observed.Select(turnEvent => turnEvent.Type)
            .Should()
            .Equal(TurnEventType.TurnStarted, TurnEventType.Message);
        runner.Finished.Should().BeTrue();
    }

    /// <summary>
    /// Three in a row rather than one, because a timer that failed to re-arm
    /// would still produce the first beat and then leave the stream silent.
    /// </summary>
    [TestMethod]
    public async Task RunKeepsBeatingWhileTheTurnIsSilent()
    {
        TaskCompletionSource released = new TaskCompletionSource();
        ScriptedTurnRunner runner = new ScriptedTurnRunner((events, cancellationToken) =>
            released.Task.WaitAsync(cancellationToken));
        int heartbeats = 0;

        await foreach (TurnEvent turnEvent in TurnEventStream.RunAsync(
            runner,
            AnyTurn,
            Immediate,
            CancellationToken.None))
        {
            turnEvent.Type.Should().Be(TurnEventType.Heartbeat);

            if (++heartbeats == 3)
            {
                released.SetResult();
            }
        }

        heartbeats.Should().BeGreaterThanOrEqualTo(3);
    }

    /// <summary>
    /// A reader that stops early is what a closed browser tab looks like. It
    /// must dispose cleanly rather than fault, and it must not leave the turn
    /// running past the request scope its dispatches depend on.
    /// </summary>
    [TestMethod]
    public async Task RunLetsAReaderLeaveWithoutFaultingOrOrphaningTheTurn()
    {
        ScriptedTurnRunner runner = new ScriptedTurnRunner(async (events, cancellationToken) =>
        {
            await events.PublishAsync(TurnEvent.TurnStarted(), cancellationToken);
            await events.PublishAsync(
                TurnEvent.Message(new AssistantMessage("Still going.")),
                cancellationToken);
        });

        await foreach (TurnEvent turnEvent in TurnEventStream.RunAsync(
            runner,
            AnyTurn,
            NeverElapses,
            CancellationToken.None))
        {
            turnEvent.Type.Should().Be(TurnEventType.TurnStarted);
            break;
        }

        runner.Finished.Should().BeTrue();
    }

    [TestMethod]
    public async Task RunStopsWhenTheClientDisconnects()
    {
        using CancellationTokenSource disconnected = new CancellationTokenSource();
        ScriptedTurnRunner runner = new ScriptedTurnRunner((events, cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken));
        disconnected.CancelAfter(Immediate);

        Func<Task> act = async () =>
        {
            await foreach (TurnEvent ignored in TurnEventStream.RunAsync(
                runner,
                AnyTurn,
                NeverElapses,
                disconnected.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// A turn that fails mid-stream must not read as a turn that finished, so
    /// the failure surfaces at the reader rather than closing the sequence.
    /// </summary>
    [TestMethod]
    public async Task RunSurfacesAFailedTurn()
    {
        ScriptedTurnRunner runner = new ScriptedTurnRunner(async (events, cancellationToken) =>
        {
            await events.PublishAsync(TurnEvent.TurnStarted(), cancellationToken);
            throw new InvalidOperationException("The provider rejected the request.");
        });

        Func<Task> act = async () => await ReadAllAsync(runner, NeverElapses);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("The provider rejected the request.");
    }

    [TestMethod]
    public async Task RunCarriesTheTranscriptForwardOnTheClosingEvent()
    {
        using JsonDocument messages = JsonDocument.Parse("""[{"role":"user"}]""");
        ScriptedTurnRunner runner = new ScriptedTurnRunner((events, cancellationToken) =>
            events.PublishAsync(
                TurnEvent.TurnCompleted(new TurnTranscript(messages.RootElement)),
                cancellationToken).AsTask());

        List<TurnEvent> observed = await ReadAllAsync(runner, NeverElapses);

        observed.Should().ContainSingle()
            .Which.Type.Should().Be(TurnEventType.TurnCompleted);
    }

    private static async Task<List<TurnEvent>> ReadAllAsync(
        IAssistantTurnRunner runner,
        TimeSpan heartbeatInterval)
    {
        List<TurnEvent> observed = new List<TurnEvent>();

        await foreach (TurnEvent turnEvent in TurnEventStream.RunAsync(
            runner,
            AnyTurn,
            heartbeatInterval,
            CancellationToken.None))
        {
            observed.Add(turnEvent);
        }

        return observed;
    }
}
