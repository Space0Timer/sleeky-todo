namespace Sleeky.Todo.Assistant.Tools;

/// <summary>
/// What a read hands the model. The version is included because every write
/// path binds versions from what was last read, and the enum-valued fields are
/// names rather than the numbers the HTTP contract uses: a model reasons about
/// "Completed", not about 2.
/// </summary>
public sealed record TodoSummary(
    Guid Id,
    string Name,
    long Version,
    DateOnly DueDate,
    string Status,
    string Priority,
    bool IsBlocked,
    bool IsDeleted);
