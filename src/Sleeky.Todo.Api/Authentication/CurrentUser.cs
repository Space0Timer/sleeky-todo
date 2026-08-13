using System.Security.Claims;

using Sleeky.Todo.Application.Abstractions.Identity;

namespace Sleeky.Todo.Api.Authentication;

internal sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        this.httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId => ReadUserId()
        ?? throw new InvalidOperationException(
            "The current request has no authenticated user.");

    public string? DisplayName => httpContextAccessor.HttpContext?.User
        .FindFirstValue(TodoClaimTypes.DisplayName);

    public bool IsAuthenticated => ReadUserId() is not null;

    private Guid? ReadUserId()
    {
        string? value = httpContextAccessor.HttpContext?.User
            .FindFirstValue(TodoClaimTypes.UserId);

        return Guid.TryParse(value, out Guid userId) && userId != Guid.Empty
            ? userId
            : null;
    }
}
