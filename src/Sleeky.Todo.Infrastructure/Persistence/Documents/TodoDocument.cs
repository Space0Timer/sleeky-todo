using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Serialization;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

[BsonIgnoreExtraElements]
internal sealed class TodoDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }

    [BsonElement(MongoTodoFields.OwnerId)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid OwnerId { get; set; }

    [BsonElement(MongoTodoFields.Name)]
    public string Name { get; set; } = string.Empty;

    [BsonElement(MongoTodoFields.NameNormalized)]
    public string NameNormalized { get; set; } = string.Empty;

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

    [BsonElement(MongoTodoFields.DependencyIds)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public List<Guid> DependencyIds { get; set; } = new List<Guid>();

    /// <summary>
    /// The searchable words of the name and description, derived by the domain
    /// tokenizer and rewritten by every full-document write.
    /// </summary>
    [BsonElement(MongoTodoFields.SearchTokens)]
    public List<string> SearchTokens { get; set; } = new List<string>();

    [BsonElement(MongoTodoFields.Recurrence)]
    public RecurrenceDocument? Recurrence { get; set; }

    [BsonElement(MongoTodoFields.SeriesId)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid? SeriesId { get; set; }

    [BsonElement(MongoTodoFields.OccurrenceNumber)]
    public int? OccurrenceNumber { get; set; }

    [BsonElement(MongoTodoFields.Version)]
    public long Version { get; set; }

    [BsonElement(MongoTodoFields.CreatedAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement(MongoTodoFields.UpdatedAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    [BsonElement(MongoTodoFields.DeletedAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? DeletedAt { get; set; }

    [BsonElement(MongoTodoFields.PurgeAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PurgeAt { get; set; }
}
