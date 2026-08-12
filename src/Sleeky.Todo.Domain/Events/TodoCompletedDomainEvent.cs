namespace Sleeky.Todo.Domain.Events;

public sealed record TodoCompletedDomainEvent : IDomainEvent
{
    public TodoCompletedDomainEvent(
        Guid todoId,
        Guid? seriesId,
        int? occurrenceNumber,
        Guid? nextOccurrenceId,
        TodoCompletionContext completionContext)
    {
        TodoId = todoId;
        SeriesId = seriesId;
        OccurrenceNumber = occurrenceNumber;
        NextOccurrenceId = nextOccurrenceId;
        CompletionContext = completionContext;
    }

    public Guid TodoId { get; }

    public Guid? SeriesId { get; }

    public int? OccurrenceNumber { get; }

    public Guid? NextOccurrenceId { get; }

    public TodoCompletionContext CompletionContext { get; }
}
