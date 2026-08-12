using FluentAssertions;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;

using Sleeky.Todo.Domain.Enums;
using Sleeky.Todo.Infrastructure.Persistence.Documents;

namespace Sleeky.Todo.IntegrationTests.Persistence;

[TestClass]
public sealed class TodoDocumentSerializationTests
{
    private static readonly DateTime Timestamp = new DateTime(
        2026,
        8,
        12,
        1,
        0,
        0,
        DateTimeKind.Utc);

    [TestMethod]
    public void TodoDocumentUsesExpectedBsonRepresentations()
    {
        TodoDocument document = CreateDocument();

        BsonDocument bsonDocument = document.ToBsonDocument();

        bsonDocument["_id"].AsString.Should().Be(document.Id);
        bsonDocument["dueDate"].BsonType.Should().Be(BsonType.String);
        bsonDocument["dueDate"].AsString.Should().Be("2026-08-31");
        bsonDocument["status"].AsString.Should().Be(nameof(TodoStatus.NotStarted));
        bsonDocument["priority"].AsString.Should().Be(nameof(TodoPriority.High));
        bsonDocument["createdAt"].BsonType.Should().Be(BsonType.DateTime);
        bsonDocument["updatedAt"].BsonType.Should().Be(BsonType.DateTime);
        bsonDocument["deletedAt"].Should().Be(BsonNull.Value);
        bsonDocument["purgeAt"].Should().Be(BsonNull.Value);
    }

    [TestMethod]
    public void TodoDocumentRoundTripsDateOnlyEnumsAndUtcTimestamps()
    {
        TodoDocument document = CreateDocument();
        BsonDocument bsonDocument = document.ToBsonDocument();

        TodoDocument deserializedDocument = BsonSerializer.Deserialize<TodoDocument>(bsonDocument);

        deserializedDocument.DueDate.Should().Be(document.DueDate);
        deserializedDocument.Status.Should().Be(document.Status);
        deserializedDocument.Priority.Should().Be(document.Priority);
        deserializedDocument.CreatedAt.Should().Be(document.CreatedAt);
        deserializedDocument.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    private static TodoDocument CreateDocument()
    {
        return new TodoDocument
        {
            Id = "todo-1",
            Name = "Submit report",
            NameNormalized = "submit report",
            Description = "Monthly report",
            DueDate = new DateOnly(2026, 8, 31),
            Status = TodoStatus.NotStarted,
            Priority = TodoPriority.High,
            DependencyIds = new List<string> { "todo-a" },
            Version = 1,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };
    }
}
