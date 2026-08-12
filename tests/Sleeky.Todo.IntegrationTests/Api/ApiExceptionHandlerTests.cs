using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Sleeky.Todo.Api.ErrorHandling;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Exceptions;

namespace Sleeky.Todo.IntegrationTests.Api;

[TestClass]
public sealed class ApiExceptionHandlerTests
{
    [TestMethod]
    public async Task UnexpectedExceptionWritesOneErrorEvent()
    {
        RecordingLogger<ApiExceptionHandler> logger = new RecordingLogger<ApiExceptionHandler>();
        ApiExceptionHandler handler = new ApiExceptionHandler(logger);
        DefaultHttpContext context = CreateHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/todos/todo-1/status";
        context.TraceIdentifier = "trace-123";
        InvalidOperationException exception = new InvalidOperationException("failure");

        bool handled = await handler.TryHandleAsync(
            context,
            exception,
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        LogEntry entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Error);
        entry.EventId.Should().Be(3001);
        entry.Exception.Should().BeSameAs(exception);
        entry.Properties["RequestMethod"].Should().Be(HttpMethods.Post);
        entry.Properties["RequestPath"].Should().Be("/api/todos/todo-1/status");
        entry.Properties["TraceId"].Should().Be("trace-123");
    }

    [TestMethod]
    public async Task ExpectedDomainConflictDoesNotWriteAnErrorEvent()
    {
        RecordingLogger<ApiExceptionHandler> logger = new RecordingLogger<ApiExceptionHandler>();
        ApiExceptionHandler handler = new ApiExceptionHandler(logger);
        DefaultHttpContext context = CreateHttpContext();
        DomainRuleException exception = new DomainRuleException(
            "Expected conflict.",
            new DomainException("Expected conflict."));

        _ = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        logger.Entries.Should().BeEmpty();
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
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
            Dictionary<string, object?> properties = state
                is IEnumerable<KeyValuePair<string, object?>> structuredState
                    ? structuredState.ToDictionary(pair => pair.Key, pair => pair.Value)
                    : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, eventId.Id, exception, properties));
        }
    }

    private sealed class LogEntry
    {
        public LogEntry(
            LogLevel level,
            int eventId,
            Exception? exception,
            IReadOnlyDictionary<string, object?> properties)
        {
            Level = level;
            EventId = eventId;
            Exception = exception;
            Properties = properties;
        }

        public int EventId { get; }

        public Exception? Exception { get; }

        public LogLevel Level { get; }

        public IReadOnlyDictionary<string, object?> Properties { get; }
    }
}
