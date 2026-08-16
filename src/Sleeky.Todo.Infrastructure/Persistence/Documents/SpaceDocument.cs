using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

[BsonIgnoreExtraElements]
internal sealed class SpaceDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }

    [BsonElement(MongoSpaceFields.Name)]
    public string Name { get; set; } = string.Empty;

    [BsonElement(MongoSpaceFields.Access)]
    public List<SpaceAccessEntryDocument> Access { get; set; } = new List<SpaceAccessEntryDocument>();

    [BsonElement(MongoSpaceFields.Version)]
    public long Version { get; set; }

    [BsonElement(MongoSpaceFields.CreatedAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement(MongoSpaceFields.UpdatedAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }
}
