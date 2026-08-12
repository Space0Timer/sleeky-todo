using FluentAssertions;

using Microsoft.Extensions.Logging;

using Sleeky.Todo.Application.Behaviors;

namespace Sleeky.Todo.Application.Tests.Behaviors;

[TestClass]
public sealed class RequestLoggingBehaviorTests
{
    [TestMethod]
    public async Task HandleWritesStructuredStartAndCompletionEvents()
    {
        RecordingLogger<RequestLoggingBehavior<TestRequest, string>> logger =
            new RecordingLogger<RequestLoggingBehavior<TestRequest, string>>();
        RequestLoggingBehavior<TestRequest, string> behavior =
            new RequestLoggingBehavior<TestRequest, string>(logger);

        string result = await behavior.Handle(
            new TestRequest("sensitive value"),
            _ => Task.FromResult("handled"),
            CancellationToken.None);

        result.Should().Be("handled");
        logger.Entries.Select(entry => entry.EventId).Should().Equal(1001, 1002);
        logger.Entries.Select(entry => entry.Level).Should().Equal(
            LogLevel.Debug,
            LogLevel.Information);
        logger.Entries.Should().OnlyContain(entry => entry.Message.Contains(
            nameof(TestRequest),
            StringComparison.Ordinal));
        logger.Entries.Should().NotContain(entry => entry.Message.Contains(
            "sensitive value",
            StringComparison.Ordinal));
    }

    private sealed class TestRequest
    {
        public TestRequest(string value)
        {
            Value = value;
        }

        public string Value { get; }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
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
            Entries.Add(new LogEntry(logLevel, eventId.Id, formatter(state, exception)));
        }
    }

    private sealed class LogEntry
    {
        public LogEntry(LogLevel level, int eventId, string message)
        {
            Level = level;
            EventId = eventId;
            Message = message;
        }

        public int EventId { get; }

        public LogLevel Level { get; }

        public string Message { get; }
    }
}
