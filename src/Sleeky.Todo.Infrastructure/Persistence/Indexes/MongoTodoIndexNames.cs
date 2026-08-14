namespace Sleeky.Todo.Infrastructure.Persistence.Indexes;

/// <summary>
/// Names of the TODO indexes that more than one component refers to.
/// </summary>
/// <remarks>
/// The search index is hinted by name at query time, so the definition and the
/// hint have to agree exactly. Sharing the constant is what makes that true by
/// construction rather than by two string literals staying in step.
/// </remarks>
internal static class MongoTodoIndexNames
{
    public const string OwnerActiveSearchTokens = "owner_active_search_tokens";
}
