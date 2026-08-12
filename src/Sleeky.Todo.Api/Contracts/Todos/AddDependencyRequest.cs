namespace Sleeky.Todo.Api.Contracts.Todos;

public sealed class AddDependencyRequest
{
    public string DependencyId { get; init; } = string.Empty;

    public long Version { get; init; }
}
