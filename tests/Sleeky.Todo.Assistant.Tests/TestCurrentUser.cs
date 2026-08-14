using Sleeky.Todo.Application.Abstractions.Identity;

namespace Sleeky.Todo.Assistant.Tests;

internal sealed class TestCurrentUser : ICurrentUser
{
    public TestCurrentUser(Guid? userId = null, string? displayName = "Sam")
    {
        this.UserId = userId ?? TestTodo.OwnerId;
        this.DisplayName = displayName;
    }

    public Guid UserId { get; }

    public string? DisplayName { get; }

    public bool IsAuthenticated => true;
}
