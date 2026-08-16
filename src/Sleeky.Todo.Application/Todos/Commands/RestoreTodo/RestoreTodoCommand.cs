using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Commands.RestoreTodo;

public sealed record RestoreTodoCommand(Guid Id, long Version) : IRequest<TodoDto>;
