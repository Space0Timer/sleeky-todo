namespace Sleeky.Todo.Infrastructure.Persistence;

internal static class MongoUserFields
{
    public const string CreatedAt = "createdAt";
    public const string DisplayName = "displayName";

    /// <summary>
    /// The lower-cased copy a prefix search matches against. Stored rather
    /// than folded at query time so the comparison is an ordinary indexed
    /// one; a case-insensitive match over the original would read the whole
    /// collection.
    /// </summary>
    public const string DisplayNameNormalized = "displayNameNormalized";
    public const string Email = "email";
    public const string EmailNormalized = "emailNormalized";
    public const string Issuer = "issuer";
    public const string LastLoginAt = "lastLoginAt";
    public const string Subject = "subject";
}
