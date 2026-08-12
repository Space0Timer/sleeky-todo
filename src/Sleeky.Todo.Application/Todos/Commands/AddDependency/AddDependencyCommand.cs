using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Commands.AddDependency;

public sealed class AddDependencyCommand : IRequest<TodoDto>
{
    public AddDependencyCommand(string id, string dependencyId, long version)
    {
        Id = id;
        DependencyId = dependencyId;
        Version = version;
    }

    public string Id { get; }

    public string DependencyId { get; }

    public long Version { get; }
}
