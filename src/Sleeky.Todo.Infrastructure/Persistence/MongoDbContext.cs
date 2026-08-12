using Microsoft.Extensions.Options;

using MongoDB.Driver;

using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence;

internal sealed class MongoDbContext
{
    public MongoDbContext(
        IMongoDatabase database,
        IOptions<MongoDbSettings> settings)
    {
        TodoItems = database.GetCollection<TodoDocument>(
            settings.Value.TodoItemsCollectionName);
    }

    public IMongoCollection<TodoDocument> TodoItems { get; }
}
