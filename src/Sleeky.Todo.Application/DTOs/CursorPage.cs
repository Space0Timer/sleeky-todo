namespace Sleeky.Todo.Application.DTOs;

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
