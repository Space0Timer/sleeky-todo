namespace Sleeky.Todo.Infrastructure.Persistence;

public sealed class MongoDbSettings
{
    public const string SectionName = "MongoDb";

    public string ConnectionString { get; init; } = string.Empty;

    public string DatabaseName { get; init; } = string.Empty;

    public string TodoItemsCollectionName { get; init; } = "todoItems";

    public string UsersCollectionName { get; init; } = "users";

    public string AssistantSettingsCollectionName { get; init; } = "assistantSettings";
}
