using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Commands.DeleteTodo;

public sealed record DeleteTodoCommand(Guid Id, long Version) : IRequest<TodoDto>;
