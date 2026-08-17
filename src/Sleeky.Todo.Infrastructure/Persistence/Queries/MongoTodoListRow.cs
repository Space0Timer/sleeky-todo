using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Serialization;

namespace Sleeky.Todo.Infrastructure.Persistence.Queries;

internal sealed class MongoTodoListRow
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }

    [BsonElement(MongoTodoFields.SpaceId)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid SpaceId { get; set; }

    [BsonElement(MongoTodoFields.CreatedByUserId)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid CreatedByUserId { get; set; }

    [BsonElement(MongoTodoFields.Name)]
    public string Name { get; set; } = string.Empty;

    [BsonElement(MongoTodoFields.Description)]
    public string? Description { get; set; }

    [BsonElement(MongoTodoFields.DueDate)]
    [BsonSerializer(typeof(DateOnlyStringSerializer))]
    public DateOnly DueDate { get; set; }

    [BsonElement(MongoTodoFields.Status)]
    [BsonRepresentation(BsonType.Int32)]
    public TodoStatus Status { get; set; }

    [BsonElement(MongoTodoFields.Priority)]
    [BsonRepresentation(BsonType.Int32)]
    public TodoPriority Priority { get; set; }

    [BsonElement(MongoTodoFields.IsRecurring)]
    public bool IsRecurring { get; set; }

    [BsonElement(MongoTodoFields.IsBlocked)]
    public bool IsBlocked { get; set; }

    [BsonElement(MongoTodoFields.IncompleteDependencyCount)]
    public int IncompleteDependencyCount { get; set; }

    [BsonElement(MongoTodoFields.Version)]
    public long Version { get; set; }

    [BsonElement(MongoTodoFields.DeletedAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? DeletedAt { get; set; }

    [BsonElement(MongoTodoFields.PurgeAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PurgeAt { get; set; }
}
