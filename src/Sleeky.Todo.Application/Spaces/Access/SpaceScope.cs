using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Spaces.Access;

/// <summary>
/// The request-scoped holder behind <see cref="ISpaceScope"/>.
/// </summary>
/// <remarks>
/// Bound by <see cref="SpaceAccessService"/> once a check has passed and by
/// nothing else. It is registered scoped, so one request sees one binding;
/// a second binding to a different Space in the same request is refused,
/// because it would mean a handler authorized for one Space dispatched work
/// in another and left the ambient scope pointing at the wrong one.
/// </remarks>
public sealed class SpaceScope : ISpaceScope
{
    private SpaceAccessContext? context;

    public bool IsBound => context is not null;

    public Guid SpaceId => Current.SpaceId;

    public string SpaceName => Current.SpaceName;

    public SpacePermission Permission => Current.Permission;

    private SpaceAccessContext Current => context
        ?? throw new InvalidOperationException(
            "No Space has been authorized for the current request.");

    /// <summary>
    /// Records the outcome of a passed access check.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A different Space is already bound for this request.
    /// </exception>
    public void Bind(SpaceAccessContext accessContext)
    {
        ArgumentNullException.ThrowIfNull(accessContext);

        if (context is not null && context.SpaceId != accessContext.SpaceId)
        {
            throw new InvalidOperationException(
                "The current request is already scoped to a different Space.");
        }

        context = accessContext;
    }
}
