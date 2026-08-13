namespace Sleeky.Todo.Application.Abstractions.Identity;

public sealed record UserIdentity
{
    public UserIdentity(Guid userId, string? displayName)
    {
        UserId = userId;
        DisplayName = displayName;
    }

    public Guid UserId { get; }

    public string? DisplayName { get; }
}
