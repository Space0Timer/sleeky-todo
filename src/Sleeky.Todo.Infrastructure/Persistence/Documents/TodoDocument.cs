using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Serialization;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

internal sealed class TodoDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("nameNormalized")]
    public string NameNormalized { get; set; } = string.Empty;

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("dueDate")]
    [BsonSerializer(typeof(DateOnlyStringSerializer))]
    public DateOnly DueDate { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public TodoStatus Status { get; set; }

    [BsonElement("priority")]
    [BsonRepresentation(BsonType.String)]
    public TodoPriority Priority { get; set; }

    [BsonElement("dependencyIds")]
    public List<string> DependencyIds { get; set; } = new List<string>();

    [BsonElement("recurrence")]
    public BsonDocument? Recurrence { get; set; }

    [BsonElement("seriesId")]
    public string? SeriesId { get; set; }

    [BsonElement("occurrenceNumber")]
    public int? OccurrenceNumber { get; set; }

    [BsonElement("version")]
    public long Version { get; set; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("deletedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? DeletedAt { get; set; }

    [BsonElement("purgeAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? PurgeAt { get; set; }
}
