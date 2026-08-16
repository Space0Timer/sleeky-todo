using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Domain.Services;

/// <summary>
/// The one comparison over <see cref="SpacePermission"/>, so no caller has to
/// know that the enum's numeric order is the permission ladder.
/// </summary>
public static class SpacePermissions
{
    /// <summary>
    /// Whether a subject holding <paramref name="granted"/> may act at
    /// <paramref name="required"/>. Every level includes the levels below it.
    /// </summary>
    public static bool Includes(SpacePermission granted, SpacePermission required)
    {
        return granted >= required;
    }
}
