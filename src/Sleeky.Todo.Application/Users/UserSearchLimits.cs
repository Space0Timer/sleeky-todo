namespace Sleeky.Todo.Application.Users;

/// <summary>
/// The bounds a user search is held to. Both exist to keep the directory from
/// being enumerated: the shortest accepted query is long enough that no single
/// letter returns a slice of everybody, and the result cap means a query that
/// does match broadly still answers with a handful.
/// </summary>
public static class UserSearchLimits
{
    public const int MaximumResults = 10;

    public const int MinimumQueryLength = 2;
}
