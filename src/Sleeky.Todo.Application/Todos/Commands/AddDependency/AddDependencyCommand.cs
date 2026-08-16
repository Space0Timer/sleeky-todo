using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Commands.AddDependency;

public sealed record AddDependencyCommand(
    Guid Id,
    Guid DependencyId,
    long Version) : IRequest<TodoDto>;
