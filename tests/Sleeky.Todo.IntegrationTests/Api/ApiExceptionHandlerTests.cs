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
        ApiExceptionTestLogger<ApiExceptionHandler> logger =
            new ApiExceptionTestLogger<ApiExceptionHandler>();
        ApiExceptionHandler handler = new ApiExceptionHandler(logger);
        DefaultHttpContext context = CreateHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/spaces/space-1/todos/todo-1/status";
        context.TraceIdentifier = "trace-123";
        InvalidOperationException exception = new InvalidOperationException("failure");

        bool handled = await handler.TryHandleAsync(
            context,
            exception,
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        ApiExceptionLogEntry entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Error);
        entry.EventId.Should().Be(3001);
        entry.Exception.Should().BeSameAs(exception);
        entry.Properties["RequestMethod"].Should().Be(HttpMethods.Post);
        entry.Properties["RequestPath"].Should().Be("/api/spaces/space-1/todos/todo-1/status");
        entry.Properties["TraceId"].Should().Be("trace-123");
    }

    [TestMethod]
    public async Task ExpectedDomainConflictDoesNotWriteAnErrorEvent()
    {
        ApiExceptionTestLogger<ApiExceptionHandler> logger =
            new ApiExceptionTestLogger<ApiExceptionHandler>();
        ApiExceptionHandler handler = new ApiExceptionHandler(logger);
        DefaultHttpContext context = CreateHttpContext();
        DomainRuleException exception = new DomainRuleException(
            "Expected conflict.",
            new DomainException("Expected conflict."));

        _ = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        logger.Entries.Should().BeEmpty();
    }

    /// <summary>
    /// A member acting above their level is told so — they can already see
    /// the Space, so there is nothing a 404 would hide — and it is an expected
    /// outcome, not an error worth an event.
    /// </summary>
    [TestMethod]
    public async Task InsufficientSpacePermissionIsForbiddenAndNotLogged()
    {
        ApiExceptionTestLogger<ApiExceptionHandler> logger =
            new ApiExceptionTestLogger<ApiExceptionHandler>();
        ApiExceptionHandler handler = new ApiExceptionHandler(logger);
        DefaultHttpContext context = CreateHttpContext();
        ForbiddenException exception = new ForbiddenException(
            "Space",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Write");

        _ = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        logger.Entries.Should().BeEmpty();
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }
}
