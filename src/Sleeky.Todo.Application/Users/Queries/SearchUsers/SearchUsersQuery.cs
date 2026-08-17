using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Users.Queries.SearchUsers;

/// <summary>
/// Finds people to share a Space with, by the start of their display name or
/// e-mail address.
/// </summary>
/// <remarks>
/// Not Space-scoped: someone is chosen before there is a grant to authorize,
/// so the only gate is that the caller is signed in. What that leaves open —
/// a signed-in user learning who else has an account — is deliberate and
/// narrow: the query has a floor, the result set has a ceiling, and only the
/// name and address a provider already published are returned.
/// </remarks>
public sealed record SearchUsersQuery(string Query) : IRequest<IReadOnlyList<UserSummaryDto>>;
