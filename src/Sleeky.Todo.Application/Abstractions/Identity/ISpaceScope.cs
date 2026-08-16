using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Abstractions.Identity;

/// <summary>
/// The Space the current request has been authorized for.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="ICurrentUser"/> for the second boundary: the
/// user says who is asking, this says which Space they were checked against.
/// It is bound only after that check passes, so persistence can read it the
/// way it reads the user — as a fact about the request, not an argument a
/// handler might forget to pass. Every member throws when nothing is bound,
/// so an unscoped query cannot silently read every Space.
/// </remarks>
public interface ISpaceScope
{
    /// <summary>
    /// Whether a Space has been bound for this request.
    /// </summary>
    bool IsBound { get; }

    /// <summary>
    /// The identifier of the authorized Space.
    /// </summary>
    /// <exception cref="InvalidOperationException">No Space is bound.</exception>
    Guid SpaceId { get; }

    /// <summary>
    /// The authorized Space's display name.
    /// </summary>
    /// <exception cref="InvalidOperationException">No Space is bound.</exception>
    string SpaceName { get; }

    /// <summary>
    /// The level the current user holds in the authorized Space.
    /// </summary>
    /// <exception cref="InvalidOperationException">No Space is bound.</exception>
    SpacePermission Permission { get; }
}
