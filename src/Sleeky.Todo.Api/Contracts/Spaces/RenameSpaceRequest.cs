namespace Sleeky.Todo.Api.Contracts.Spaces;

public sealed class RenameSpaceRequest
{
    public string Name { get; init; } = string.Empty;

    public long Version { get; init; }
}
