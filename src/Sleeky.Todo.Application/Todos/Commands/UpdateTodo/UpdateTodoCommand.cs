using MediatR;

using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Todos.Commands.UpdateTodo;

public sealed record UpdateTodoCommand(
    Guid Id,
    string Name,
    string? Description,
    DateOnly DueDate,
    TodoPriority Priority,
    long Version) : IRequest<TodoDto>;
