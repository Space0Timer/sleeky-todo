using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Commands.RemoveDependency;

public sealed record RemoveDependencyCommand(
    Guid Id,
    Guid DependencyId,
    long Version) : IRequest<TodoDto>;
