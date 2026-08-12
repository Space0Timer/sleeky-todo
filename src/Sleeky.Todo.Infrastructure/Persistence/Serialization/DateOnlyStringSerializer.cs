using System.Globalization;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Sleeky.Todo.Infrastructure.Persistence.Serialization;

internal sealed class DateOnlyStringSerializer : SerializerBase<DateOnly>
{
    private const string DateFormat = "yyyy-MM-dd";

    public override DateOnly Deserialize(
        BsonDeserializationContext context,
        BsonDeserializationArgs args)
    {
        if (context.Reader.GetCurrentBsonType() != BsonType.String)
        {
            throw new BsonSerializationException("A date-only value must be stored as a BSON string.");
        }

        string value = context.Reader.ReadString();

        if (!DateOnly.TryParseExact(
            value,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly date))
        {
            throw new BsonSerializationException($"'{value}' is not a valid date-only value.");
        }

        return date;
    }

    public override void Serialize(
        BsonSerializationContext context,
        BsonSerializationArgs args,
        DateOnly value)
    {
        context.Writer.WriteString(value.ToString(DateFormat, CultureInfo.InvariantCulture));
    }
}
