using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

internal sealed class RecurrenceDocument
{
    [BsonElement(MongoRecurrenceFields.Type)]
    [BsonRepresentation(BsonType.String)]
    public RecurrenceType Type { get; set; }

    [BsonElement(MongoRecurrenceFields.Interval)]
    public int Interval { get; set; }

    [BsonElement(MongoRecurrenceFields.Unit)]
    [BsonRepresentation(BsonType.String)]
    public RecurrenceUnit Unit { get; set; }

    [BsonElement(MongoRecurrenceFields.AnchorDay)]
    public int? AnchorDay { get; set; }
}
