using System.Globalization;

using Microsoft.Extensions.Options;

using MongoDB.Bson;
using MongoDB.Driver;

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

    private static readonly string[] PriorityOrder =
    [
        nameof(TodoPriority.Low),
        nameof(TodoPriority.Medium),
        nameof(TodoPriority.High),
    ];

    private static readonly string[] StatusOrder =
    [
        nameof(TodoStatus.NotStarted),
        nameof(TodoStatus.InProgress),
        nameof(TodoStatus.Completed),
        nameof(TodoStatus.Archived),
    ];

    private readonly IMongoCollection<TodoDocument> todoItems;
    private readonly string collectionName;

    public MongoTodoListReader(
        IMongoDatabase database,
        IOptions<MongoDbSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(settings);

        this.collectionName = settings.Value.TodoItemsCollectionName;
        this.todoItems = database.GetCollection<TodoDocument>(this.collectionName);
    }

    public async Task<IReadOnlyList<TodoListItemDto>> GetTodosAsync(
        TodoListCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        List<BsonDocument> stages =
        [
            new BsonDocument("$match", BuildFilter(criteria)),
            BuildDependencyLookupStage(collectionName),
            BuildIncompleteDependencyCountStage(),
            new BsonDocument(
                "$set",
                new BsonDocument(
                    "isBlocked",
                    new BsonDocument(
                        "$gt",
                        new BsonArray { "$incompleteDependencyCount", 0 }))),
        ];

        if (criteria.DependencyStatus.HasValue)
        {
            stages.Add(
                new BsonDocument(
                    "$match",
                    new BsonDocument(
                        "isBlocked",
                        criteria.DependencyStatus == TodoDependencyStatus.Blocked)));
        }

        stages.Add(
            new BsonDocument(
                "$set",
                new BsonDocument("sortValue", BuildSortValue(criteria.SortField))));

        if (criteria.LastSortValue is not null && criteria.LastTodoId is not null)
        {
            stages.Add(new BsonDocument("$match", BuildCursorFilter(criteria)));
        }

        int sortDirection = criteria.SortDirection == TodoSortDirection.Asc ? 1 : -1;
        stages.Add(
            new BsonDocument(
                "$sort",
                new BsonDocument
                {
                    { "sortValue", sortDirection },
                    { "_id", sortDirection },
                }));
        stages.Add(new BsonDocument("$limit", criteria.Limit));
        stages.Add(
            new BsonDocument(
                "$project",
                new BsonDocument
                {
                    { "dependencyDocuments", 0 },
                    { "sortValue", 0 },
                }));

        IReadOnlyList<BsonDocument> documents = await todoItems
            .Aggregate<BsonDocument>(stages)
            .ToListAsync(cancellationToken);

        return documents.Select(ToDto).ToArray();
    }

    private static BsonDocument BuildFilter(TodoListCriteria criteria)
    {
        BsonArray filters = new BsonArray();
        filters.Add(criteria.Scope switch
        {
            TodoListScope.Active => new BsonDocument(
                "$and",
                new BsonArray
                {
                    new BsonDocument("deletedAt", BsonNull.Value),
                    new BsonDocument(
                        "status",
                        new BsonDocument("$ne", nameof(TodoStatus.Archived))),
                }),
            TodoListScope.Archived => new BsonDocument(
                "$and",
                new BsonArray
                {
                    new BsonDocument("deletedAt", BsonNull.Value),
                    new BsonDocument("status", nameof(TodoStatus.Archived)),
                }),
            TodoListScope.Deleted => new BsonDocument(
                "deletedAt",
                new BsonDocument("$type", "date")),
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        });

        if (criteria.Status.HasValue)
        {
            filters.Add(new BsonDocument("status", criteria.Status.Value.ToString()));
        }

        if (criteria.Priority.HasValue)
        {
            filters.Add(new BsonDocument("priority", criteria.Priority.Value.ToString()));
        }

        if (criteria.DueFrom.HasValue || criteria.DueTo.HasValue)
        {
            BsonDocument dueDateFilter = new BsonDocument();
            if (criteria.DueFrom.HasValue)
            {
                dueDateFilter.Add(
                    "$gte",
                    criteria.DueFrom.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture));
            }

            if (criteria.DueTo.HasValue)
            {
                dueDateFilter.Add(
                    "$lte",
                    criteria.DueTo.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture));
            }

            filters.Add(new BsonDocument("dueDate", dueDateFilter));
        }

        return filters.Count == 1
            ? filters[0].AsBsonDocument
            : new BsonDocument("$and", filters);
    }

    private static BsonDocument BuildDependencyLookupStage(string todoCollectionName)
    {
        return new BsonDocument(
            "$lookup",
            new BsonDocument
            {
                { "from", todoCollectionName },
                { "localField", "dependencyIds" },
                { "foreignField", "_id" },
                { "as", "dependencyDocuments" },
            });
    }

    private static BsonDocument BuildIncompleteDependencyCountStage()
    {
        BsonDocument dependencyIds = new BsonDocument(
            "$ifNull",
            new BsonArray { "$dependencyIds", new BsonArray() });
        BsonDocument missingDependencyCount = new BsonDocument(
            "$subtract",
            new BsonArray
            {
                new BsonDocument("$size", dependencyIds),
                new BsonDocument("$size", "$dependencyDocuments"),
            });
        BsonDocument incompleteDocuments = new BsonDocument(
            "$filter",
            new BsonDocument
            {
                { "input", "$dependencyDocuments" },
                { "as", "dependency" },
                {
                    "cond",
                    new BsonDocument(
                        "$or",
                        new BsonArray
                        {
                            new BsonDocument(
                                "$ne",
                                new BsonArray { "$$dependency.deletedAt", BsonNull.Value }),
                            new BsonDocument(
                                "$ne",
                                new BsonArray
                                {
                                    "$$dependency.status",
                                    nameof(TodoStatus.Completed),
                                }),
                        })
                },
            });

        return new BsonDocument(
            "$set",
            new BsonDocument(
                "incompleteDependencyCount",
                new BsonDocument(
                    "$add",
                    new BsonArray
                    {
                        missingDependencyCount,
                        new BsonDocument("$size", incompleteDocuments),
                    })));
    }

    private static BsonValue BuildSortValue(TodoSortField sortField)
    {
        return sortField switch
        {
            TodoSortField.DueDate => "$dueDate",
            TodoSortField.Priority => BuildOrderExpression(
                "$priority",
                PriorityOrder),
            TodoSortField.Status => BuildOrderExpression(
                "$status",
                StatusOrder),
            TodoSortField.Name => "$nameNormalized",
            _ => throw new ArgumentOutOfRangeException(nameof(sortField)),
        };
    }

    private static BsonDocument BuildOrderExpression(
        string field,
        IReadOnlyList<string> orderedValues)
    {
        BsonArray branches = new BsonArray(
            orderedValues.Select((value, index) =>
                new BsonDocument
                {
                    {
                        "case",
                        new BsonDocument("$eq", new BsonArray { field, value })
                    },
                    { "then", index },
                }));

        return new BsonDocument(
            "$switch",
            new BsonDocument
            {
                { "branches", branches },
                { "default", orderedValues.Count },
            });
    }

    private static BsonDocument BuildCursorFilter(TodoListCriteria criteria)
    {
        string comparison = criteria.SortDirection == TodoSortDirection.Asc ? "$gt" : "$lt";
        BsonValue lastSortValue = ParseSortValue(
            criteria.LastSortValue!,
            criteria.SortField);

        return new BsonDocument(
            "$or",
            new BsonArray
            {
                new BsonDocument(
                    "sortValue",
                    new BsonDocument(comparison, lastSortValue)),
                new BsonDocument(
                    "$and",
                    new BsonArray
                    {
                        new BsonDocument("sortValue", lastSortValue),
                        new BsonDocument(
                            "_id",
                            new BsonDocument(
                                comparison,
                                new BsonBinaryData(
                                    criteria.LastTodoId.GetValueOrDefault(),
                                    GuidRepresentation.Standard))),
                    }),
            });
    }

    private static BsonValue ParseSortValue(string value, TodoSortField sortField)
    {
        return sortField switch
        {
            TodoSortField.Priority or TodoSortField.Status => int.Parse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture),
            _ => value,
        };
    }

    private static TodoListItemDto ToDto(BsonDocument document)
    {
        string? description = GetNullableString(document, "description");
        bool isRecurring = document.TryGetValue(
            "recurrence",
            out BsonValue? recurrence)
            && !recurrence.IsBsonNull
            && !(recurrence.IsBsonDocument
                && recurrence.AsBsonDocument.TryGetValue(
                    "_csharpnull",
                    out BsonValue? csharpNull)
                && csharpNull.IsBoolean
                && csharpNull.AsBoolean);

        return new TodoListItemDto(
            document["_id"].AsBsonBinaryData.ToGuid(),
            document["name"].AsString,
            CreateDescriptionPreview(description),
            DateOnly.ParseExact(
                document["dueDate"].AsString,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture),
            Enum.Parse<TodoStatus>(document["status"].AsString),
            Enum.Parse<TodoPriority>(document["priority"].AsString),
            isRecurring,
            document["isBlocked"].AsBoolean,
            document["incompleteDependencyCount"].AsInt32,
            document["version"].ToInt64(),
            GetNullableDateTimeOffset(document, "deletedAt"),
            GetNullableDateTimeOffset(document, "purgeAt"));
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

    private static string? GetNullableString(BsonDocument document, string field)
    {
        return document.TryGetValue(field, out BsonValue? value) && !value.IsBsonNull
            ? value.AsString
            : null;
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(
        BsonDocument document,
        string field)
    {
        return document.TryGetValue(field, out BsonValue? value) && !value.IsBsonNull
            ? new DateTimeOffset(value.ToUniversalTime())
            : null;
    }
}
