namespace Sleeky.Todo.Application.Abstractions.Persistence;

/// <summary>
/// One position in a recurring series: the series and the occurrence number
/// within it, which together name a TODO uniquely inside a Space.
/// </summary>
/// <remarks>
/// A completion asks the repository which of these already exist before it
/// inserts a successor, so re-completing a reopened occurrence whose successor
/// is already there writes only the completion instead of colliding with the
/// unique series index.
/// </remarks>
public sealed record TodoSeriesOccurrence(Guid SeriesId, int OccurrenceNumber);
