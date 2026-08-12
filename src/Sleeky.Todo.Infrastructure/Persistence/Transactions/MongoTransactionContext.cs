using MongoDB.Driver;

namespace Sleeky.Todo.Infrastructure.Persistence.Transactions;

public sealed class MongoTransactionContext
{
    public IClientSessionHandle? Session { get; internal set; }
}
