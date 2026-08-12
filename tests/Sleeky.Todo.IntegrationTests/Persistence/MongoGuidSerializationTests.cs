using FluentAssertions;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;

using Sleeky.Todo.Infrastructure.Persistence.Repositories;

namespace Sleeky.Todo.IntegrationTests.Persistence;

[TestClass]
public sealed class MongoGuidSerializationTests
{
    [TestMethod]
    public void TodoDocumentStoresStandardUuidsAndIgnoresExtraElements()
    {
        Guid todoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid dependencyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid seriesId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Type documentType = GetRequiredTodoDocumentType();
        object document = Activator.CreateInstance(documentType)
            ?? throw new InvalidOperationException("Could not create TodoDocument.");
        documentType.GetProperty("Id")?.SetValue(document, todoId);
        documentType.GetProperty("DependencyIds")?.SetValue(
            document,
            new List<Guid> { dependencyId });
        documentType.GetProperty("SeriesId")?.SetValue(document, seriesId);

        BsonDocument bson = document.ToBsonDocument(documentType);
        bson["futureTodoField"] = "ignored";
        bson["recurrence"] = new BsonDocument
        {
            { "type", "Daily" },
            { "interval", 1 },
            { "unit", "Days" },
            { "anchorDay", BsonNull.Value },
            { "futureRecurrenceField", "ignored" },
        };
        object roundTripped = BsonSerializer.Deserialize(bson, documentType);

        AssertStandardUuid(bson["_id"], todoId);
        AssertStandardUuid(bson["dependencyIds"].AsBsonArray[0], dependencyId);
        AssertStandardUuid(bson["seriesId"], seriesId);
        documentType.GetProperty("Id")?.GetValue(roundTripped).Should().Be(todoId);
        documentType.GetProperty("DependencyIds")?.GetValue(roundTripped).Should()
            .BeEquivalentTo(new List<Guid> { dependencyId });
        documentType.GetProperty("SeriesId")?.GetValue(roundTripped).Should().Be(seriesId);
    }

    private static Type GetRequiredTodoDocumentType()
    {
        return typeof(MongoTodoRepository).Assembly.GetType(
            "Sleeky.Todo.Infrastructure.Persistence.Documents.TodoDocument",
            throwOnError: true)
            ?? throw new InvalidOperationException("Could not find TodoDocument.");
    }

    private static void AssertStandardUuid(BsonValue value, Guid expected)
    {
        BsonBinaryData binary = value.AsBsonBinaryData;

        binary.SubType.Should().Be(BsonBinarySubType.UuidStandard);
        binary.ToGuid().Should().Be(expected);
    }
}
