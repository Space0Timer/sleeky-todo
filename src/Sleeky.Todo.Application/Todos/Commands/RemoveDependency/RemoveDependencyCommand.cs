using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Commands.RemoveDependency;

public sealed class RemoveDependencyCommand : IRequest<TodoDto>
{
    public RemoveDependencyCommand(Guid id, Guid dependencyId, long version)
    {
        Id = id;
        DependencyId = dependencyId;
        Version = version;
    }

    public Guid Id { get; }

    public Guid DependencyId { get; }

    public long Version { get; }
}
