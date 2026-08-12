using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Commands.AddDependency;

public sealed class AddDependencyCommand : IRequest<TodoDto>
{
    public AddDependencyCommand(Guid id, Guid dependencyId, long version)
    {
        Id = id;
        DependencyId = dependencyId;
        Version = version;
    }

    public Guid Id { get; }

    public Guid DependencyId { get; }

    public long Version { get; }
}
