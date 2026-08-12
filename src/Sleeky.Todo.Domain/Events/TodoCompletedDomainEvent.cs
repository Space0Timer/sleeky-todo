namespace Sleeky.Todo.Domain.Events;

public sealed record TodoCompletedDomainEvent : IDomainEvent
{
    public TodoCompletedDomainEvent(
        string todoId,
        string? seriesId,
        int? occurrenceNumber,
        string? nextOccurrenceId,
        TodoCompletionContext completionContext)
    {
        TodoId = todoId;
        SeriesId = seriesId;
        OccurrenceNumber = occurrenceNumber;
        NextOccurrenceId = nextOccurrenceId;
        CompletionContext = completionContext;
    }

    public string TodoId { get; }

    public string? SeriesId { get; }

    public int? OccurrenceNumber { get; }

    public string? NextOccurrenceId { get; }

    public TodoCompletionContext CompletionContext { get; }
}
