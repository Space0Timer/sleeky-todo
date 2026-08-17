using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.Exceptions;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Application.Todos.Recurrence;

/// <summary>
/// Builds the next occurrence of a recurring TODO from the completion that
/// triggered it.
/// </summary>
/// <remarks>
/// The successor is created here and inserted by the calling handler in the
/// same transaction as the completion, so neither exists without the other.
/// It lives in the Application layer because building it needs the date rules
/// of <c>IRecurrenceCalculator</c> and the caller's write, not because the
/// entity could not describe it.
/// </remarks>
public interface IRecurringOccurrenceFactory
{
    /// <summary>
    /// Returns the TODO that follows the one <paramref name="completion"/>
    /// describes.
    /// </summary>
    /// <remarks>
    /// The successor takes the identifier the completion already fixed as
    /// <see cref="TodoCompletion.NextOccurrenceId"/>, so a caller can report it
    /// without asking the factory. It copies Space, creator, name, description,
    /// priority, schedule, and series, advances the occurrence number by one,
    /// and is due one schedule step after the <em>scheduled</em> date rather
    /// than after the completion instant. Its creation timestamp is the
    /// completion instant. Dependencies are not copied: the next occurrence
    /// starts unblocked.
    /// </remarks>
    /// <exception cref="DomainException">
    /// The completion carries no schedule, or is missing the series, occurrence
    /// number, or successor identifier a schedule requires.
    /// </exception>
    TodoItem CreateNext(TodoCompletion completion);
}
