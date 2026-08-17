using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

internal static class TodoDocumentMapper
{
    public static TodoDocument FromDomain(TodoItem todoItem, long? version = null)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        return new TodoDocument
        {
            Id = todoItem.Id,
            SpaceId = todoItem.SpaceId,
            CreatedByUserId = todoItem.CreatedByUserId,
            Name = todoItem.Name,
            NameNormalized = todoItem.NameNormalized,
            Description = todoItem.Description,
            DueDate = todoItem.DueDate,
            Status = todoItem.Status,
            Priority = todoItem.Priority,
            DependencyIds = todoItem.DependencyIds.ToList(),
            SearchTokens = todoItem.SearchTokens.ToList(),
            Recurrence = todoItem.Recurrence is null
                ? null
                : new RecurrenceDocument
                {
                    Type = todoItem.Recurrence.Type,
                    Interval = todoItem.Recurrence.Interval,
                    Unit = todoItem.Recurrence.Unit,
                    AnchorDay = todoItem.Recurrence.AnchorDay,
                },
            SeriesId = todoItem.SeriesId,
            OccurrenceNumber = todoItem.OccurrenceNumber,
            Version = version ?? todoItem.Version,
            CreatedAt = todoItem.CreatedAt.UtcDateTime,
            UpdatedAt = todoItem.UpdatedAt.UtcDateTime,
            DeletedAt = todoItem.DeletedAt?.UtcDateTime,
            PurgeAt = todoItem.PurgeAt?.UtcDateTime,
        };
    }

    public static TodoItem ToDomain(TodoDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        RecurrenceSchedule? recurrence = document.Recurrence is null
            ? null
            : RecurrenceSchedule.Rehydrate(
                document.Recurrence.Type,
                document.Recurrence.Interval,
                document.Recurrence.Unit,
                document.Recurrence.AnchorDay);

        return TodoItem.Rehydrate(
            document.Id,
            document.SpaceId,
            document.CreatedByUserId,
            document.Name,
            document.Description,
            document.DueDate,
            document.Status,
            document.Priority,
            document.DependencyIds,
            recurrence,
            document.SeriesId,
            document.OccurrenceNumber,
            document.Version,
            ToDateTimeOffset(document.CreatedAt),
            ToDateTimeOffset(document.UpdatedAt),
            ToDateTimeOffset(document.DeletedAt),
            ToDateTimeOffset(document.PurgeAt));
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        return value.HasValue ? ToDateTimeOffset(value.Value) : null;
    }
}
