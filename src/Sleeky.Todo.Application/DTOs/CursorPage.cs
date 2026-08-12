namespace Sleeky.Todo.Application.DTOs;

public sealed class CursorPage<T>
{
    public CursorPage(IReadOnlyList<T> items, string? nextCursor)
    {
        Items = items;
        NextCursor = nextCursor;
    }

    public IReadOnlyList<T> Items { get; }

    public string? NextCursor { get; }
}
