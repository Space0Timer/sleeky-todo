using MediatR;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Users.Queries.SearchUsers;

public sealed class SearchUsersQueryHandler
    : IRequestHandler<SearchUsersQuery, IReadOnlyList<UserSummaryDto>>
{
    private readonly ICurrentUser currentUser;
    private readonly IUserDirectoryRepository users;

    public SearchUsersQueryHandler(
        IUserDirectoryRepository users,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(currentUser);

        this.users = users;
        this.currentUser = currentUser;
    }

    public async Task<IReadOnlyList<UserSummaryDto>> Handle(
        SearchUsersQuery request,
        CancellationToken cancellationToken)
    {
        Guid callerId = currentUser.UserId;

        // One more than the cap, because the caller is dropped afterwards:
        // asking for exactly the cap would return nine results whenever the
        // searcher's own name matched, which reads as a missing person rather
        // than as the searcher recognising themselves.
        IReadOnlyCollection<UserSearchMatch> matches = await users.SearchAsync(
            request.Query.Trim(),
            UserSearchLimits.MaximumResults + 1,
            cancellationToken);

        return matches
            .Where(match => match.UserId != callerId)
            .Take(UserSearchLimits.MaximumResults)
            .Select(match => new UserSummaryDto(match.UserId, match.DisplayName, match.Email))
            .ToArray();
    }
}
