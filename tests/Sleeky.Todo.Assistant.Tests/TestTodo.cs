using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Assistant.Tests;

internal static class TestTodo
{
    public static readonly DateOnly DueDate = new DateOnly(2026, 8, 31);

    public static readonly DateTimeOffset Timestamp =
        new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    public static readonly Guid OwnerId = Id("owner-1");

    public static Guid Id(string value)
    {
        byte[] bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));

        return new Guid(bytes);
    }

    public static TodoDto At(Guid id, long version, string name = "Submit report")
    {
        return TodoDto.FromEntity(Rehydrate(id, version, name, deleted: false));
    }

    public static TodoDto Deleted(Guid id, long version, string name = "Submit report")
    {
        return TodoDto.FromEntity(Rehydrate(id, version, name, deleted: true));
    }

    private static TodoItem Rehydrate(Guid id, long version, string name, bool deleted)
    {
        return TodoItem.Rehydrate(
            id,
            OwnerId,
            name,
            description: null,
            DueDate,
            TodoStatus.NotStarted,
            TodoPriority.High,
            Array.Empty<Guid>(),
            recurrence: null,
            seriesId: null,
            occurrenceNumber: null,
            version,
            Timestamp,
            Timestamp,
            deleted ? Timestamp : null,
            deleted ? Timestamp.AddDays(90) : null);
    }
}
