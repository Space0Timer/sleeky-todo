namespace Sleeky.Todo.Api.Contracts.Todos;

public sealed class AddDependencyRequest
{
    public Guid DependencyId { get; init; }

    public long Version { get; init; }
}
