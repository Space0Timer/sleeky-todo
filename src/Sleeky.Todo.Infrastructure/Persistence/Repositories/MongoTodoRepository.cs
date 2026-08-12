using Microsoft.Extensions.Options;

using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.Infrastructure.Persistence.Repositories;

public sealed class MongoTodoRepository : ITodoRepository
{
    private readonly IMongoCollection<TodoDocument> todoItems;

    public MongoTodoRepository(
        IMongoDatabase database,
        IOptions<MongoDbSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(settings);

        todoItems = database.GetCollection<TodoDocument>(
            settings.Value.TodoItemsCollectionName);
    }

    public async Task AddAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default)
    {
        TodoDocument document = TodoDocumentMapper.FromDomain(todoItem);
        await todoItems.InsertOneAsync(document, cancellationToken: cancellationToken);
    }

    public async Task<TodoItem?> GetByIdAsync(
        string id,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<TodoDocument> filter = BuildIdFilter(id, includeDeleted);
        TodoDocument? document = await todoItems
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : TodoDocumentMapper.ToDomain(document);
    }

    public async Task<bool> ExistsAsync(
        string id,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<TodoDocument> filter = BuildIdFilter(id, includeDeleted);
        long count = await todoItems.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public async Task<IReadOnlyCollection<TodoItem>> GetByIdsAsync(
        IEnumerable<string> ids,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        string[] distinctIds = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctIds.Length == 0)
        {
            return Array.Empty<TodoItem>();
        }

        FilterDefinition<TodoDocument> filter =
            Builders<TodoDocument>.Filter.In(document => document.Id, distinctIds);
        if (!includeDeleted)
        {
            filter &= Builders<TodoDocument>.Filter.Eq(document => document.DeletedAt, null);
        }

        List<TodoDocument> documents = await todoItems
            .Find(filter)
            .ToListAsync(cancellationToken);

        return documents.Select(TodoDocumentMapper.ToDomain).ToArray();
    }

    public async Task<bool> HasActiveDependentsAsync(
        string dependencyId,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<TodoDocument> filter =
            Builders<TodoDocument>.Filter.AnyEq(
                document => document.DependencyIds,
                dependencyId)
            & Builders<TodoDocument>.Filter.Eq(document => document.DeletedAt, null)
            & Builders<TodoDocument>.Filter.Ne(
                document => document.Status,
                TodoStatus.Archived);
        long count = await todoItems.CountDocumentsAsync(
            filter,
            new CountOptions { Limit = 1 },
            cancellationToken);

        return count > 0;
    }

    public Task<TodoItem?> UpdateAsync(
        TodoItem todoItem,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<TodoDocument> filter = BuildMutationFilter(
            todoItem.Id,
            expectedVersion,
            includeDeleted: false);

        return ReplaceAsync(todoItem, expectedVersion, filter, cancellationToken);
    }

    public Task<TodoItem?> SoftDeleteAsync(
        TodoItem todoItem,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (todoItem.DeletedAt is null || todoItem.PurgeAt is null)
        {
            throw new InvalidOperationException(
                "A TODO must be soft-deleted before it can be persisted as deleted.");
        }

        FilterDefinition<TodoDocument> filter = BuildMutationFilter(
            todoItem.Id,
            expectedVersion,
            includeDeleted: false);

        return ReplaceAsync(todoItem, expectedVersion, filter, cancellationToken);
    }

    public Task<TodoItem?> RestoreAsync(
        TodoItem todoItem,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (todoItem.DeletedAt is not null || todoItem.PurgeAt is not null)
        {
            throw new InvalidOperationException(
                "A TODO must be restored before it can be persisted as active.");
        }

        FilterDefinition<TodoDocument> filter =
            Builders<TodoDocument>.Filter.Eq(document => document.Id, todoItem.Id)
            & Builders<TodoDocument>.Filter.Eq(document => document.Version, expectedVersion)
            & Builders<TodoDocument>.Filter.Ne(document => document.DeletedAt, null);

        return ReplaceAsync(todoItem, expectedVersion, filter, cancellationToken);
    }

    private static FilterDefinition<TodoDocument> BuildIdFilter(
        string id,
        bool includeDeleted)
    {
        FilterDefinition<TodoDocument> filter =
            Builders<TodoDocument>.Filter.Eq(document => document.Id, id);

        if (!includeDeleted)
        {
            filter &= Builders<TodoDocument>.Filter.Eq(document => document.DeletedAt, null);
        }

        return filter;
    }

    private static FilterDefinition<TodoDocument> BuildMutationFilter(
        string id,
        long expectedVersion,
        bool includeDeleted)
    {
        return BuildIdFilter(id, includeDeleted)
            & Builders<TodoDocument>.Filter.Eq(document => document.Version, expectedVersion);
    }

    private async Task<TodoItem?> ReplaceAsync(
        TodoItem todoItem,
        long expectedVersion,
        FilterDefinition<TodoDocument> filter,
        CancellationToken cancellationToken)
    {
        if (todoItem.Version != expectedVersion)
        {
            return null;
        }

        long nextVersion = checked(expectedVersion + 1);
        TodoDocument replacement = TodoDocumentMapper.FromDomain(todoItem, nextVersion);
        FindOneAndReplaceOptions<TodoDocument> options = new FindOneAndReplaceOptions<TodoDocument>
        {
            ReturnDocument = ReturnDocument.After,
        };
        TodoDocument? persistedDocument = await todoItems.FindOneAndReplaceAsync(
            filter,
            replacement,
            options,
            cancellationToken);

        return persistedDocument is null
            ? null
            : TodoDocumentMapper.ToDomain(persistedDocument);
    }
}
