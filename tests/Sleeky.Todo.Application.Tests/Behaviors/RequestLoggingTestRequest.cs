namespace Sleeky.Todo.Application.Tests.Behaviors;

internal sealed class RequestLoggingTestRequest
{
    public RequestLoggingTestRequest(string value)
    {
        Value = value;
    }

    public string Value { get; }
}
