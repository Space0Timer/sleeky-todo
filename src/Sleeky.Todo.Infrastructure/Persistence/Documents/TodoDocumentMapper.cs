using Sleeky.Todo.Domain.Entities;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

internal static class TodoDocumentMapper
{
    public static TodoDocument FromDomain(TodoItem todoItem, long? version = null)
    {
        ArgumentNullException.ThrowIfNull(todoItem);

        if (todoItem.Recurrence is not null)
        {
            throw new NotSupportedException("Recurrence persistence has not been implemented.");
        }

        return new TodoDocument
        {
            Id = todoItem.Id,
            Name = todoItem.Name,
            NameNormalized = todoItem.NameNormalized,
            Description = todoItem.Description,
            DueDate = todoItem.DueDate,
            Status = todoItem.Status,
            Priority = todoItem.Priority,
            DependencyIds = todoItem.DependencyIds.ToList(),
            Recurrence = null,
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

        if (document.Recurrence is not null)
        {
            throw new NotSupportedException("Recurrence persistence has not been implemented.");
        }

        return TodoItem.Rehydrate(
            document.Id,
            document.Name,
            document.Description,
            document.DueDate,
            document.Status,
            document.Priority,
            document.DependencyIds,
            null,
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
