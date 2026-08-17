namespace Sleeky.Todo.Application.DTOs;

/// <summary>
/// One person as offered to someone choosing who to share a Space with:
/// enough to recognise them and to name them in a grant, nothing else the
/// directory holds.
/// </summary>
public sealed record UserSummaryDto(Guid Id, string? DisplayName, string? Email);
