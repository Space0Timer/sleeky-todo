using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Todos.Queries.GetTodo;

public sealed record GetTodoQuery(Guid Id) : IRequest<TodoDto>;
