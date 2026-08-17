namespace Sleeky.Todo.Application.Abstractions.Identity;

/// <summary>
/// A directory entry a search matched.
/// </summary>
/// <remarks>
/// Distinct from <see cref="UserIdentity"/>, which every other directory read
/// answers with: those reads project the two fields a principal and an access
/// list need, and carrying an e-mail on that record would report null for a
/// user whose address the directory holds. A search reads the field, so it
/// says so in its own type.
/// </remarks>
public sealed record UserSearchMatch(Guid UserId, string? DisplayName, string? Email);
