namespace Sleeky.Todo.Domain.Enums;

/// <summary>
/// What a subject may do inside a Space.
/// </summary>
/// <remarks>
/// The levels form a ladder, and the numeric order is that ladder: each level
/// includes everything below it, so Owner may do what Write may, and Write may
/// do what Read may. <see cref="Services.SpacePermissions.Includes"/> is the
/// one place that comparison is written.
/// </remarks>
public enum SpacePermission
{
    Read = 1,
    Write = 2,
    Owner = 3,
}
