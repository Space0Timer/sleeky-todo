using Sleeky.Todo.Application.Abstractions.Identity;

namespace Sleeky.Todo.Application.Tests;

internal sealed class TestCurrentUser : ICurrentUser
{
    public TestCurrentUser(Guid userId, string? displayName = "Test User")
    {
        UserId = userId;
        DisplayName = displayName;
    }

    public Guid UserId { get; }

    public string? DisplayName { get; }

    public bool IsAuthenticated => UserId != Guid.Empty;
}
