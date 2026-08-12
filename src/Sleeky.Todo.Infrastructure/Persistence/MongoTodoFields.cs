namespace Sleeky.Todo.Infrastructure.Persistence;

internal static class MongoTodoFields
{
    public const string CompletedDependencies = "completedDependencies";
    public const string CreatedAt = "createdAt";
    public const string DateFormat = "yyyy-MM-dd";
    public const string DeletedAt = "deletedAt";
    public const string DependencyIds = "dependencyIds";
    public const string Description = "description";
    public const string DueDate = "dueDate";
    public const string Id = "_id";
    public const string IncompleteDependencyCount = "incompleteDependencyCount";
    public const string IsBlocked = "isBlocked";
    public const string IsRecurring = "isRecurring";
    public const string Name = "name";
    public const string NameNormalized = "nameNormalized";
    public const string OccurrenceNumber = "occurrenceNumber";
    public const string Priority = "priority";
    public const string PurgeAt = "purgeAt";
    public const string Recurrence = "recurrence";
    public const string SeriesId = "seriesId";
    public const string Status = "status";
    public const string UpdatedAt = "updatedAt";
    public const string Version = "version";
}
