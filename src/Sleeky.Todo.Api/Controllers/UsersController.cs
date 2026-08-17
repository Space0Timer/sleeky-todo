using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Users.Queries.SearchUsers;

namespace Sleeky.Todo.Api.Controllers;

/// <summary>
/// Finding the person to share a Space with.
/// </summary>
/// <remarks>
/// One route, and it is the only place the API answers a question about
/// someone the caller has no relationship with, so it is kept as small as it
/// can be: a signed-in caller, a term of at least two characters, at most ten
/// answers, and only the display name and address the identity provider
/// already published. The directory holds only users who have signed in at
/// least once, so a colleague who has never opened the application cannot be
/// found — and cannot be shared with.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly ISender sender;

    public UsersController(ISender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);

        this.sender = sender;
    }

    [HttpGet("search")]
    [ProducesResponseType<IReadOnlyList<UserSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> Search(
        [FromQuery(Name = "q")] string? q,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<UserSummaryDto> users = await sender.Send(
            new SearchUsersQuery(q ?? string.Empty),
            cancellationToken);

        return Ok(users);
    }
}
