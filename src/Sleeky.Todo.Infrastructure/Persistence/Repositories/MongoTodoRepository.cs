using Microsoft.Extensions.Options;

using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Documents;
using Sleeky.Todo.Infrastructure.Persistence.Transactions;

namespace Sleeky.Todo.Infrastructure.Persistence.Repositories;

public sealed class MongoTodoRepository : ITodoRepository
{
    private readonly ICurrentUser currentUser;
    private readonly IMongoCollection<TodoDocument> todoItems;
    private readonly MongoTransactionContext transactionContext;

    public MongoTodoRepository(
        IMongoDatabase database,
        IOptions<MongoDbSettings> settings,
        ICurrentUser currentUser,
        MongoTransactionContext? transactionContext = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(currentUser);

        this.todoItems = database.GetCollection<TodoDocument>(
            settings.Value.TodoItemsCollectionName);
        this.currentUser = currentUser;
        this.transactionContext = transactionContext ?? new MongoTransactionContext();
    }

    private Guid OwnerId => currentUser.UserId;

    public async Task AddAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        if (todoItem.OwnerId != OwnerId)
        {
            throw new InvalidOperationException(
                "A TODO can only be persisted by its owner.");
        }

        TodoDocument document = TodoDocumentMapper.FromDomain(todoItem);
        if (transactionContext.Session is null)
        {
            await todoItems.InsertOneAsync(
                document,
                cancellationToken: cancellationToken);
            return;
        }

        await todoItems.InsertOneAsync(
            transactionContext.Session,
            document,
            cancellationToken: cancellationToken);
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

        FilterDefinition<TodoDocument> filter = BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.In(document => document.Id, distinctIds);
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
        FilterDefinition<TodoDocument> filter = BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.AnyEq(
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

    public Task<TodoItem> UpdateAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        FilterDefinition<TodoDocument> filter = BuildMutationFilter(
            todoItem.Id,
            todoItem.Version,
            includeDeleted: false);

        return ReplaceAsync(todoItem, filter, cancellationToken);
    }

    public Task<TodoItem> SoftDeleteAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        if (todoItem.DeletedAt is null || todoItem.PurgeAt is null)
        {
            throw new InvalidOperationException(
                "A TODO must be soft-deleted before it can be persisted as deleted.");
        }

        FilterDefinition<TodoDocument> filter = BuildMutationFilter(
            todoItem.Id,
            todoItem.Version,
            includeDeleted: false);

        return ReplaceAsync(todoItem, filter, cancellationToken);
    }

    public Task<TodoItem> RestoreAsync(
        TodoItem todoItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        if (todoItem.DeletedAt is not null || todoItem.PurgeAt is not null)
        {
            throw new InvalidOperationException(
                "A TODO must be restored before it can be persisted as active.");
        }

        FilterDefinition<TodoDocument> filter = BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.Eq(document => document.Id, todoItem.Id)
            & Builders<TodoDocument>.Filter.Eq(document => document.Version, todoItem.Version)
            & Builders<TodoDocument>.Filter.Ne(document => document.DeletedAt, null);

        return ReplaceAsync(todoItem, filter, cancellationToken);
    }

    private FilterDefinition<TodoDocument> BuildOwnerFilter()
    {
        return Builders<TodoDocument>.Filter.Eq(document => document.OwnerId, OwnerId);
    }

    private FilterDefinition<TodoDocument> BuildIdFilter(
        Guid id,
        bool includeDeleted)
    {
        FilterDefinition<TodoDocument> filter = BuildOwnerFilter()
            & Builders<TodoDocument>.Filter.Eq(document => document.Id, id);

        if (!includeDeleted)
        {
            filter &= Builders<TodoDocument>.Filter.Eq(document => document.DeletedAt, null);
        }

        return filter;
    }

    private FilterDefinition<TodoDocument> BuildMutationFilter(
        Guid id,
        long expectedVersion,
        bool includeDeleted)
    {
        return BuildIdFilter(id, includeDeleted)
            & Builders<TodoDocument>.Filter.Eq(document => document.Version, expectedVersion);
    }

    /// <summary>
    /// Replaces the stored document only while it still carries the version the
    /// aggregate was loaded at, so a concurrent writer cannot be overwritten.
    /// </summary>
    private async Task<TodoItem> ReplaceAsync(
        TodoItem todoItem,
        FilterDefinition<TodoDocument> filter,
        CancellationToken cancellationToken)
    {
        long expectedVersion = todoItem.Version;
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
            ? throw new ConcurrencyConflictException("TODO", todoItem.Id, expectedVersion)
            : TodoDocumentMapper.ToDomain(persistedDocument);
    }
}
