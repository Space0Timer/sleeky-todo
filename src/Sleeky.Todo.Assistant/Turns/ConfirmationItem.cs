using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Assistant.Turns;

/// <summary>
/// One TODO in a destructive proposal, as it stood when the proposal was made.
/// The name is here so the user confirms against something readable, and the
/// version so that what they confirm is what gets sent.
/// </summary>
public sealed record ConfirmationItem(
    Guid Id,
    string Name,
    long Version,
    TodoStatus Status,
    DateTimeOffset? DeletedAt);
