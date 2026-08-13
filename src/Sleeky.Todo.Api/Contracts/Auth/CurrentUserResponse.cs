namespace Sleeky.Todo.Api.Contracts.Auth;

public sealed class CurrentUserResponse
{
    public CurrentUserResponse(bool isAuthenticated, Guid? userId, string? displayName)
    {
        IsAuthenticated = isAuthenticated;
        UserId = userId;
        DisplayName = displayName;
    }

    public bool IsAuthenticated { get; }

    public Guid? UserId { get; }

    public string? DisplayName { get; }
}
