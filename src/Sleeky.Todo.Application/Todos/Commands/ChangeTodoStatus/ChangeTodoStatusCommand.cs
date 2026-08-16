using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;

public sealed record ChangeTodoStatusCommand(
    Guid Id,
    TodoStatus Status,
    long Version) : IRequest<TodoDto>;
