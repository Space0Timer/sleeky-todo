using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Spaces.Queries.GetSpaces;

/// <summary>
/// Every Space the current user is a member of, oldest first, with the level
/// they hold in each.
/// </summary>
/// <remarks>
/// Not Space-scoped: it is how a user finds their Spaces in the first place.
/// It also ensures the user's personal Space exists, so a first sign-in
/// always has somewhere to start.
/// </remarks>
public sealed record GetSpacesQuery : IRequest<IReadOnlyList<SpaceSummaryDto>>;
