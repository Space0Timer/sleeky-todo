using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Domain.ValueObjects;

/// <summary>
/// What a TODO decided when it completed. Nothing subscribes to this: the
/// aggregate records it on itself so the application handler can act on it,
/// which is why it is a completion rather than a domain event.
/// </summary>
/// <param name="ScheduledDueDate">
/// The due date the completed occurrence was scheduled for, not when it was
/// completed, so a late completion does not drift the series.
/// </param>
/// <param name="NextOccurrenceId">
/// Minted by the aggregate at completion, so the insert of the successor and
/// the response describing it agree on the identifier before either happens.
/// Null when the completed TODO does not recur.
/// </param>
public sealed record TodoCompletion(
    Guid TodoId,
    Guid OwnerId,
    string Name,
    string? Description,
    DateOnly ScheduledDueDate,
    TodoPriority Priority,
    RecurrenceSchedule? Recurrence,
    DateTimeOffset CompletedAt,
    Guid? SeriesId,
    int? OccurrenceNumber,
    Guid? NextOccurrenceId);
