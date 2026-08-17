using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

[BsonIgnoreExtraElements]
internal sealed class UserDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }

    [BsonElement(MongoUserFields.Issuer)]
    public string Issuer { get; set; } = string.Empty;

    [BsonElement(MongoUserFields.Subject)]
    public string Subject { get; set; } = string.Empty;

    [BsonElement(MongoUserFields.DisplayName)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// <see cref="DisplayName"/> lower-cased, written whenever the name is.
    /// The searchable copy is kept beside the readable one so a search never
    /// has to transform the field it filters on.
    /// </summary>
    [BsonElement(MongoUserFields.DisplayNameNormalized)]
    public string? DisplayNameNormalized { get; set; }

    [BsonElement(MongoUserFields.Email)]
    public string? Email { get; set; }

    [BsonElement(MongoUserFields.EmailNormalized)]
    public string? EmailNormalized { get; set; }

    [BsonElement(MongoUserFields.CreatedAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement(MongoUserFields.LastLoginAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastLoginAt { get; set; }
}
