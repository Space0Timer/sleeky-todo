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
        RequestLoggingTestLogger<RequestLoggingBehavior<RequestLoggingTestRequest, string>> logger =
            new RequestLoggingTestLogger<
                RequestLoggingBehavior<RequestLoggingTestRequest, string>>();
        RequestLoggingBehavior<RequestLoggingTestRequest, string> behavior =
            new RequestLoggingBehavior<RequestLoggingTestRequest, string>(logger);

        string result = await behavior.Handle(
            new RequestLoggingTestRequest("sensitive value"),
            _ => Task.FromResult("handled"),
            CancellationToken.None);

        result.Should().Be("handled");
        logger.Entries.Select(entry => entry.EventId).Should().Equal(1001, 1002);
        logger.Entries.Select(entry => entry.Level).Should().Equal(
            LogLevel.Debug,
            LogLevel.Information);
        logger.Entries.Should().OnlyContain(entry => entry.Message.Contains(
            nameof(RequestLoggingTestRequest),
            StringComparison.Ordinal));
        logger.Entries.Should().NotContain(entry => entry.Message.Contains(
            "sensitive value",
            StringComparison.Ordinal));
    }
}
