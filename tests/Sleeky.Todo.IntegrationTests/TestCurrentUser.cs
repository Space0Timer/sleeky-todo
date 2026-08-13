using Sleeky.Todo.Application.Abstractions.Identity;

namespace Sleeky.Todo.IntegrationTests;

internal sealed class TestCurrentUser : ICurrentUser
{
    private readonly Guid userId;

    public TestCurrentUser(Guid userId, string? displayName = "Test User")
    {
        this.userId = userId;
        DisplayName = displayName;
    }

    /// <summary>
    /// Gets the internal user identifier, honouring the production contract
    /// that reading it without an authenticated user is a failure rather than
    /// an unscoped query.
    /// </summary>
    public Guid UserId => userId == Guid.Empty
        ? throw new InvalidOperationException(
            "The current request has no authenticated user.")
        : userId;

    public string? DisplayName { get; }

    public bool IsAuthenticated => userId != Guid.Empty;
}
