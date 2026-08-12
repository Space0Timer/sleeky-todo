namespace Sleeky.Todo.Application.Tests.Behaviors;

internal sealed class TestRequest
{
    public TestRequest(string value)
    {
        Value = value;
    }

    public string Value { get; }
}
