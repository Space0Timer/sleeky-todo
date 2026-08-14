using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

/// <summary>
/// One user's assistant configuration, keyed by the user's own identifier: a
/// user has one, so there is no separate document identity to carry.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class AssistantSettingsDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid UserId { get; set; }

    [BsonElement(MongoAssistantSettingsFields.Provider)]
    public string Provider { get; set; } = string.Empty;

    [BsonElement(MongoAssistantSettingsFields.BaseUrl)]
    public string? BaseUrl { get; set; }

    [BsonElement(MongoAssistantSettingsFields.Model)]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Ciphertext produced by the assistant's key protector. Persistence never
    /// holds the plaintext and cannot decrypt this.
    /// </summary>
    [BsonElement(MongoAssistantSettingsFields.ProtectedApiKey)]
    public string? ProtectedApiKey { get; set; }

    [BsonElement(MongoAssistantSettingsFields.UpdatedAt)]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }
}
