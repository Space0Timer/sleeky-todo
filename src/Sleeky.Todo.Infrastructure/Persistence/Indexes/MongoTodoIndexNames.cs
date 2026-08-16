namespace Sleeky.Todo.Infrastructure.Persistence.Indexes;

/// <summary>
/// Names of the TODO indexes that more than one component refers to.
/// </summary>
/// <remarks>
/// A hint naming an index that does not exist is rejected by the server rather
/// than downgraded to another plan, so a searching query fails outright if its
/// index is missing or renamed. Holding the name here, rather than on the
/// initializer that creates it, keeps the definition and the hint in step
/// without pointing a query-path class at a startup component.
///
/// The two cannot drift apart today because creation runs in process at
/// startup. That is the coupling to carry forward if creation ever moves to a
/// deployment step, where a host could serve requests before the index exists.
/// </remarks>
internal static class MongoTodoIndexNames
{
    public const string OwnerActiveSearchTokens = "owner_active_search_tokens";
}
