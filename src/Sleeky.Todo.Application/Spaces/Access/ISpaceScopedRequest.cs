using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Access;

/// <summary>
/// A request that acts inside one Space and must be authorized against it
/// before its handler runs.
/// </summary>
/// <remarks>
/// Implementing this is the whole of what a command or query does about
/// authorization. <see cref="SpaceAccessBehavior{TRequest, TResponse}"/> reads
/// the two members, performs the check, and binds
/// <see cref="Abstractions.Identity.ISpaceScope"/>; the handler itself never
/// calls anything. A request that forgets to implement it is not scoped at
/// all — and its repository reads then fail on the unbound scope rather than
/// returning every Space's data.
/// </remarks>
public interface ISpaceScopedRequest
{
    Guid SpaceId { get; }

    /// <summary>
    /// The lowest level at which the request may run. Queries ask for
    /// <see cref="SpacePermission.Read"/>, mutations for
    /// <see cref="SpacePermission.Write"/>, membership changes for
    /// <see cref="SpacePermission.Owner"/>.
    /// </summary>
    SpacePermission RequiredPermission { get; }
}
