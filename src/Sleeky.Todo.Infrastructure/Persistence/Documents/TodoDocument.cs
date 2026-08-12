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
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public List<Guid> DependencyIds { get; set; } = new List<Guid>();

    [BsonElement("recurrence")]
    public RecurrenceDocument? Recurrence { get; set; }

    [BsonElement("seriesId")]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid? SeriesId { get; set; }

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
