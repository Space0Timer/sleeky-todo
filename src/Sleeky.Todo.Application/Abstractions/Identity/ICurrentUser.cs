namespace Sleeky.Todo.Application.Abstractions.Identity;

public interface ICurrentUser
{
    /// <summary>
    /// Gets the internal identifier of the authenticated user.
    /// Throws <see cref="InvalidOperationException"/> when no user is
    /// authenticated, so an unscoped query cannot silently read every owner.
    /// </summary>
    Guid UserId { get; }

    string? DisplayName { get; }

    bool IsAuthenticated { get; }
}
