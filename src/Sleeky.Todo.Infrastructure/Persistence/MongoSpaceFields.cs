namespace Sleeky.Todo.Infrastructure.Persistence;

internal static class MongoSpaceFields
{
    public const string Access = "access";
    public const string CreatedAt = "createdAt";
    public const string Id = "_id";
    public const string Name = "name";
    public const string Permission = "permission";
    public const string SubjectId = "subjectId";
    public const string SubjectType = "subjectType";
    public const string UpdatedAt = "updatedAt";
    public const string Version = "version";

    /// <summary>
    /// The dotted paths into the embedded access list, which is what the
    /// membership index and the membership filter are built on.
    /// </summary>
    public const string AccessSubjectId = Access + "." + SubjectId;
    public const string AccessSubjectType = Access + "." + SubjectType;
}
