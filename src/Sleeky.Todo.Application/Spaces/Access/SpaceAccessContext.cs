using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Access;

/// <summary>
/// What an access check established: which Space, and at what level the
/// current user holds it. Returned so a caller that needs the name or the
/// level — the assistant's opening context, for one — does not read the
/// Space a second time.
/// </summary>
public sealed record SpaceAccessContext(
    Guid SpaceId,
    string SpaceName,
    SpacePermission Permission);
