using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

/// <summary>
/// One embedded line of a Space's access list. Embedded rather than a
/// collection of its own because it is only ever read with its Space and
/// written under its Space's version.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class SpaceAccessEntryDocument
{
    [BsonElement(MongoSpaceFields.SubjectId)]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid SubjectId { get; set; }

    [BsonElement(MongoSpaceFields.SubjectType)]
    [BsonRepresentation(BsonType.Int32)]
    public SubjectType SubjectType { get; set; }

    [BsonElement(MongoSpaceFields.Permission)]
    [BsonRepresentation(BsonType.Int32)]
    public SpacePermission Permission { get; set; }
}
