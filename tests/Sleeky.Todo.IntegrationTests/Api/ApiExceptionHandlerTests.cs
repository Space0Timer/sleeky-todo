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
        InvalidOperationException exception = new InvalidOperationException("failure");

        bool handled = await handler.TryHandleAsync(
            context,
            exception,
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        LogEntry entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeSameAs(exception);
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
            Entries.Add(new LogEntry(logLevel, exception));
        }
    }

    private sealed class LogEntry
    {
        public LogEntry(LogLevel level, Exception? exception)
        {
            Level = level;
            Exception = exception;
        }

        public Exception? Exception { get; }

        public LogLevel Level { get; }
    }
}
