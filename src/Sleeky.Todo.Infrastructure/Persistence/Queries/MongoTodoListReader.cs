using System.Globalization;
using System.Linq.Expressions;

using Microsoft.Extensions.Options;

using MongoDB.Bson;
using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Identity;
using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Documents;

using TodoSortDirection = Sleeky.Todo.Application.Todos.Queries.GetTodos.SortDirection;

namespace Sleeky.Todo.Infrastructure.Persistence.Queries;

public sealed class MongoTodoListReader : ITodoListReader
{
    private const int DescriptionPreviewLength = 120;

    private readonly string collectionName;
    private readonly ICurrentUser currentUser;
    private readonly IMongoCollection<TodoDocument> todoItems;

    public MongoTodoListReader(
        IMongoDatabase database,
        IOptions<MongoDbSettings> settings,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(currentUser);

        this.collectionName = settings.Value.TodoItemsCollectionName;
        this.currentUser = currentUser;
        this.todoItems = database.GetCollection<TodoDocument>(this.collectionName);
    }

    public async Task<IReadOnlyList<TodoListItemDto>> GetTodosAsync(
        TodoListCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        Guid ownerId = currentUser.UserId;
        IAggregateFluent<TodoDocument> filteredTodos = BuildFilteredPipeline(
            this.todoItems,
            criteria,
            ownerId);
        IAggregateFluent<MongoTodoListRow> query = criteria.DependencyStatus.HasValue
            ? BuildDependencyFilteredQuery(
                filteredTodos,
                criteria,
                this.collectionName,
                ownerId)
            : BuildPageFirstQuery(
                filteredTodos,
                criteria,
                this.collectionName,
                ownerId);
        List<MongoTodoListRow> rows = await query.ToListAsync(cancellationToken);

        return rows.ConvertAll(ToDto);
    }

    private static IAggregateFluent<TodoDocument> BuildFilteredPipeline(
        IMongoCollection<TodoDocument> todoItems,
        TodoListCriteria criteria,
        Guid ownerId)
    {
        IAggregateFluent<TodoDocument> pipeline = todoItems
            .Aggregate()
            .Match(BuildFilter(criteria, ownerId));

        if (criteria.LastSortValue is null || criteria.LastTodoId is null)
        {
            return pipeline;
        }

        return pipeline.Match(BuildCursorFilter(criteria));
    }

    private static IAggregateFluent<MongoTodoListRow> BuildPageFirstQuery(
        IAggregateFluent<TodoDocument> pipeline,
        TodoListCriteria criteria,
        string collectionName,
        Guid ownerId)
    {
        IAggregateFluent<TodoDocument> page = ApplySortAndLimit(pipeline, criteria);
        IAggregateFluent<BsonDocument> withDependencyState = AddDependencyState(
            page,
            collectionName,
            ownerId);

        return ProjectRows(withDependencyState);
    }

    private static IAggregateFluent<MongoTodoListRow> BuildDependencyFilteredQuery(
        IAggregateFluent<TodoDocument> pipeline,
        TodoListCriteria criteria,
        string collectionName,
        Guid ownerId)
    {
        IAggregateFluent<BsonDocument> withDependencyState = AddDependencyState(
            pipeline,
            collectionName,
            ownerId)
            .Match(BuildDependencyStatusFilter(criteria.DependencyStatus!.Value));
        IAggregateFluent<TodoDocument> filteredDocuments =
            withDependencyState.As<TodoDocument>();
        IAggregateFluent<TodoDocument> page = ApplySortAndLimit(
            filteredDocuments,
            criteria);

        return ProjectRows(page);
    }

    private static IAggregateFluent<BsonDocument> AddDependencyState(
        IAggregateFluent<TodoDocument> pipeline,
        string collectionName,
        Guid ownerId)
    {
        return pipeline
            .AppendStage(CreateStage<TodoDocument, BsonDocument>(
                BuildCompletedDependencyLookupStage(collectionName, ownerId)))
            .AppendStage(CreateStage<BsonDocument, BsonDocument>(
                BuildIncompleteDependencyCountStage()));
    }

    private static IAggregateFluent<TodoDocument> ApplySortAndLimit(
        IAggregateFluent<TodoDocument> pipeline,
        TodoListCriteria criteria)
    {
        return pipeline
            .Sort(BuildSort(criteria.SortField, criteria.SortDirection))
            .Limit(criteria.Limit);
    }

    private static IAggregateFluent<MongoTodoListRow> ProjectRows<TDocument>(
        IAggregateFluent<TDocument> pipeline)
    {
        return pipeline.AppendStage(CreateStage<TDocument, MongoTodoListRow>(
            BuildProjectionStage()));
    }

    private static FilterDefinition<TodoDocument> BuildFilter(
        TodoListCriteria criteria,
        Guid ownerId)
    {
        FilterDefinitionBuilder<TodoDocument> filters = Builders<TodoDocument>.Filter;
        FilterDefinition<TodoDocument> filter =
            filters.Eq(todo => todo.OwnerId, ownerId)
            & BuildScopeFilter(criteria.Scope);

        if (criteria.Status.HasValue)
        {
            filter &= filters.Eq(todo => todo.Status, criteria.Status.Value);
        }

        if (criteria.Priority.HasValue)
        {
            filter &= filters.Eq(todo => todo.Priority, criteria.Priority.Value);
        }

        if (criteria.DueFrom.HasValue)
        {
            filter &= filters.Gte(todo => todo.DueDate, criteria.DueFrom.Value);
        }

        if (criteria.DueTo.HasValue)
        {
            filter &= filters.Lte(todo => todo.DueDate, criteria.DueTo.Value);
        }

        return filter;
    }

    private static FilterDefinition<TodoDocument> BuildScopeFilter(TodoListScope scope)
    {
        FilterDefinitionBuilder<TodoDocument> filters = Builders<TodoDocument>.Filter;

        return scope switch
        {
            TodoListScope.Active =>
                filters.Eq(todo => todo.DeletedAt, null)
                & filters.Ne(todo => todo.Status, TodoStatus.Archived),
            TodoListScope.Archived =>
                filters.Eq(todo => todo.DeletedAt, null)
                & filters.Eq(todo => todo.Status, TodoStatus.Archived),
            TodoListScope.Deleted => filters.Type(
                todo => todo.DeletedAt,
                BsonType.DateTime),
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
    }

    private static FilterDefinition<TodoDocument> BuildCursorFilter(
        TodoListCriteria criteria)
    {
        string lastSortValue = criteria.LastSortValue!;
        Guid lastTodoId = criteria.LastTodoId!.Value;

        return criteria.SortField switch
        {
            TodoSortField.DueDate => BuildCursorFilterForField(
                todo => todo.DueDate,
                DateOnly.ParseExact(
                    lastSortValue,
                    MongoTodoFields.DateFormat,
                    CultureInfo.InvariantCulture),
                lastTodoId,
                criteria.SortDirection),
            TodoSortField.Priority => BuildCursorFilterForField(
                todo => todo.Priority,
                (TodoPriority)ParseNumericSortValue(lastSortValue),
                lastTodoId,
                criteria.SortDirection),
            TodoSortField.Status => BuildCursorFilterForField(
                todo => todo.Status,
                (TodoStatus)ParseNumericSortValue(lastSortValue),
                lastTodoId,
                criteria.SortDirection),
            TodoSortField.Name => BuildCursorFilterForField(
                todo => todo.NameNormalized,
                lastSortValue,
                lastTodoId,
                criteria.SortDirection),
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        };
    }

    private static FilterDefinition<TodoDocument> BuildCursorFilterForField<TValue>(
        Expression<Func<TodoDocument, TValue>> field,
        TValue lastSortValue,
        Guid lastTodoId,
        TodoSortDirection direction)
    {
        FilterDefinitionBuilder<TodoDocument> filters = Builders<TodoDocument>.Filter;
        FilterDefinition<TodoDocument> valueComparison = direction == TodoSortDirection.Asc
            ? filters.Gt(field, lastSortValue)
            : filters.Lt(field, lastSortValue);
        FilterDefinition<TodoDocument> idComparison = direction == TodoSortDirection.Asc
            ? filters.Gt(todo => todo.Id, lastTodoId)
            : filters.Lt(todo => todo.Id, lastTodoId);

        return valueComparison
            | (filters.Eq(field, lastSortValue) & idComparison);
    }

    private static SortDefinition<TodoDocument> BuildSort(
        TodoSortField field,
        TodoSortDirection direction)
    {
        SortDefinitionBuilder<TodoDocument> sorts = Builders<TodoDocument>.Sort;
        SortDefinition<TodoDocument> primarySort = direction == TodoSortDirection.Asc
            ? BuildAscendingSort(field)
            : BuildDescendingSort(field);
        SortDefinition<TodoDocument> idSort = direction == TodoSortDirection.Asc
            ? sorts.Ascending(todo => todo.Id)
            : sorts.Descending(todo => todo.Id);

        return sorts.Combine(primarySort, idSort);
    }

    private static SortDefinition<TodoDocument> BuildAscendingSort(TodoSortField field)
    {
        SortDefinitionBuilder<TodoDocument> sorts = Builders<TodoDocument>.Sort;

        return field switch
        {
            TodoSortField.DueDate => sorts.Ascending(todo => todo.DueDate),
            TodoSortField.Priority => sorts.Ascending(todo => todo.Priority),
            TodoSortField.Status => sorts.Ascending(todo => todo.Status),
            TodoSortField.Name => sorts.Ascending(todo => todo.NameNormalized),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static SortDefinition<TodoDocument> BuildDescendingSort(TodoSortField field)
    {
        SortDefinitionBuilder<TodoDocument> sorts = Builders<TodoDocument>.Sort;

        return field switch
        {
            TodoSortField.DueDate => sorts.Descending(todo => todo.DueDate),
            TodoSortField.Priority => sorts.Descending(todo => todo.Priority),
            TodoSortField.Status => sorts.Descending(todo => todo.Status),
            TodoSortField.Name => sorts.Descending(todo => todo.NameNormalized),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    private static int ParseNumericSortValue(string value)
    {
        return int.Parse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture);
    }

    private static BsonDocument BuildCompletedDependencyLookupStage(
        string collectionName,
        Guid ownerId)
    {
        return new BsonDocument(
            "$lookup",
            new BsonDocument
            {
                { "from", collectionName },
                { "localField", MongoTodoFields.DependencyIds },
                { "foreignField", MongoTodoFields.Id },
                {
                    "pipeline",
                    new BsonArray
                    {
                        new BsonDocument(
                            "$match",
                            new BsonDocument
                            {
                                {
                                    MongoTodoFields.OwnerId,
                                    new BsonBinaryData(
                                        ownerId,
                                        GuidRepresentation.Standard)
                                },
                                { MongoTodoFields.DeletedAt, BsonNull.Value },
                                { MongoTodoFields.Status, (int)TodoStatus.Completed },
                            }),
                        new BsonDocument(
                            "$project",
                            new BsonDocument(MongoTodoFields.Id, 1)),
                    }
                },
                { "as", MongoTodoFields.CompletedDependencies },
            });
    }

    private static BsonDocument BuildIncompleteDependencyCountStage()
    {
        BsonDocument dependencyIds = new BsonDocument(
            "$ifNull",
            new BsonArray
            {
                FieldPath(MongoTodoFields.DependencyIds),
                new BsonArray(),
            });

        return new BsonDocument(
            "$set",
            new BsonDocument(
                MongoTodoFields.IncompleteDependencyCount,
                new BsonDocument(
                    "$subtract",
                    new BsonArray
                    {
                        new BsonDocument("$size", dependencyIds),
                        new BsonDocument(
                            "$size",
                            FieldPath(MongoTodoFields.CompletedDependencies)),
                    })));
    }

    private static FilterDefinition<BsonDocument> BuildDependencyStatusFilter(
        TodoDependencyStatus status)
    {
        FilterDefinitionBuilder<BsonDocument> filters = Builders<BsonDocument>.Filter;

        return status switch
        {
            TodoDependencyStatus.Blocked => filters.Gt(
                MongoTodoFields.IncompleteDependencyCount,
                0),
            TodoDependencyStatus.Unblocked => filters.Eq(
                MongoTodoFields.IncompleteDependencyCount,
                0),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    private static BsonDocument BuildProjectionStage()
    {
        return new BsonDocument(
            "$project",
            new BsonDocument
            {
                { MongoTodoFields.Id, 1 },
                { MongoTodoFields.Name, 1 },
                { MongoTodoFields.Description, 1 },
                { MongoTodoFields.DueDate, 1 },
                { MongoTodoFields.Status, 1 },
                { MongoTodoFields.Priority, 1 },
                {
                    MongoTodoFields.IsRecurring,
                    new BsonDocument(
                        "$ne",
                        new BsonArray
                        {
                            new BsonDocument(
                                "$ifNull",
                                new BsonArray
                                {
                                    FieldPath(MongoTodoFields.Recurrence),
                                    BsonNull.Value,
                                }),
                            BsonNull.Value,
                        })
                },
                {
                    MongoTodoFields.IsBlocked,
                    new BsonDocument(
                        "$gt",
                        new BsonArray
                        {
                            FieldPath(MongoTodoFields.IncompleteDependencyCount),
                            0,
                        })
                },
                { MongoTodoFields.IncompleteDependencyCount, 1 },
                { MongoTodoFields.Version, 1 },
                { MongoTodoFields.DeletedAt, 1 },
                { MongoTodoFields.PurgeAt, 1 },
            });
    }

    private static PipelineStageDefinition<TInput, TOutput> CreateStage<TInput, TOutput>(
        BsonDocument stage)
    {
        return new BsonDocumentPipelineStageDefinition<TInput, TOutput>(stage);
    }

    private static string FieldPath(string field)
    {
        return string.Concat("$", field);
    }

    private static TodoListItemDto ToDto(MongoTodoListRow row)
    {
        return new TodoListItemDto(
            row.Id,
            row.Name,
            CreateDescriptionPreview(row.Description),
            row.DueDate,
            row.Status,
            row.Priority,
            row.IsRecurring,
            row.IsBlocked,
            row.IncompleteDependencyCount,
            row.Version,
            ToDateTimeOffset(row.DeletedAt),
            ToDateTimeOffset(row.PurgeAt));
    }

    private static string? CreateDescriptionPreview(string? description)
    {
        if (description is null || description.Length <= DescriptionPreviewLength)
        {
            return description;
        }

        return string.Concat(
            description.AsSpan(0, DescriptionPreviewLength - 3),
            "...");
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        DateTime utcValue = DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        return new DateTimeOffset(utcValue);
    }
}
