using Microsoft.Extensions.Options;

using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Documents;
using Sleeky.Todo.Infrastructure.Persistence.Transactions;

namespace Sleeky.Todo.Infrastructure.Persistence.Repositories;

public sealed class MongoTodoRepository : ITodoRepository
{
    private readonly IMongoCollection<TodoDocument> todoItems;
    private readonly MongoTransactionContext transactionContext;

    public MongoTodoRepository(
        IMongoDatabase database,
        IOptions<MongoDbSettings> settings,
        MongoTransactionContext? transactionContext = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(settings);

        this.todoItems = database.GetCollection<TodoDocument>(
            settings.Value.TodoItemsCollectionName);
        this.transactionContext = transactionContext ?? new MongoTransactionContext();
    }

    public async Task AddAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default)
    {
        TodoDocument document = TodoDocumentMapper.FromDomain(todoItem);
        if (transactionContext.Session is null)
        {
            await todoItems.InsertOneAsync(
                document,
                cancellationToken: cancellationToken);
        }
        else
        {
            await todoItems.InsertOneAsync(
                transactionContext.Session,
                document,
                cancellationToken: cancellationToken);
        }
    }

    public async Task<TodoItem?> GetByIdAsync(
        Guid id,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<TodoDocument> filter = BuildIdFilter(id, includeDeleted);
        IFindFluent<TodoDocument, TodoDocument> find = transactionContext.Session is null
            ? todoItems.Find(filter)
            : todoItems.Find(transactionContext.Session, filter);
        TodoDocument? document = await find.FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : TodoDocumentMapper.ToDomain(document);
    }

    public async Task<bool> ExistsAsync(
        Guid id,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        FilterDefinition<TodoDocument> filter = BuildIdFilter(id, includeDeleted);
        long count = transactionContext.Session is null
            ? await todoItems.CountDocumentsAsync(
                filter,
                new CountOptions { Limit = 1 },
                cancellationToken)
            : await todoItems.CountDocumentsAsync(
                transactionContext.Session,
                filter,
                new CountOptions { Limit = 1 },
                cancellationToken);

        return count > 0;
    }

    public async Task<IReadOnlyCollection<TodoItem>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        Guid[] distinctIds = ids.Distinct().ToArray();
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

        IFindFluent<TodoDocument, TodoDocument> find = transactionContext.Session is null
            ? todoItems.Find(filter)
            : todoItems.Find(transactionContext.Session, filter);
        List<TodoDocument> documents = await find.ToListAsync(cancellationToken);

        return documents.Select(TodoDocumentMapper.ToDomain).ToArray();
    }

    public async Task<bool> HasActiveDependentsAsync(
        Guid dependencyId,
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
        long count = transactionContext.Session is null
            ? await todoItems.CountDocumentsAsync(
                filter,
                new CountOptions { Limit = 1 },
                cancellationToken)
            : await todoItems.CountDocumentsAsync(
                transactionContext.Session,
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
        Guid id,
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
        Guid id,
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
        TodoDocument? persistedDocument = transactionContext.Session is null
            ? await todoItems.FindOneAndReplaceAsync(
                filter,
                replacement,
                options,
                cancellationToken)
            : await todoItems.FindOneAndReplaceAsync(
                transactionContext.Session,
                filter,
                replacement,
                options,
                cancellationToken);

        return persistedDocument is null
            ? null
            : TodoDocumentMapper.ToDomain(persistedDocument);
    }
}
