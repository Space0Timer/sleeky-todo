using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

[BsonIgnoreExtraElements]
internal sealed class RecurrenceDocument
{
    [BsonElement("type")]
    [BsonRepresentation(BsonType.String)]
    public RecurrenceType Type { get; set; }

    [BsonElement("interval")]
    public int Interval { get; set; }

    [BsonElement("unit")]
    [BsonRepresentation(BsonType.String)]
    public RecurrenceUnit Unit { get; set; }

    [BsonElement("anchorDay")]
    public int? AnchorDay { get; set; }
}
