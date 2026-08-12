using MediatR;

using Microsoft.AspNetCore.Mvc;

using Sleeky.Todo.Api.Contracts.Todos;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
using Sleeky.Todo.Application.Todos.Commands.DeleteTodo;
using Sleeky.Todo.Application.Todos.Commands.RestoreTodo;
using Sleeky.Todo.Application.Todos.Commands.UpdateTodo;
using Sleeky.Todo.Application.Todos.Queries.GetTodo;

namespace Sleeky.Todo.Api.Controllers;

[ApiController]
[Route("api/todos")]
public sealed class TodosController : ControllerBase
{
    private readonly ISender sender;

    public TodosController(ISender sender)
    {
        this.sender = sender;
    }

    [HttpPost]
    [ProducesResponseType<TodoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TodoDto>> Create(
        CreateTodoRequest request,
        CancellationToken cancellationToken)
    {
        CreateTodoCommand command = new CreateTodoCommand(
            request.Name,
            request.Description,
            request.DueDate,
            request.Priority);
        TodoDto todo = await sender.Send(command, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = todo.Id }, todo);
    }

    [HttpGet("{id}")]
    [ProducesResponseType<TodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TodoDto>> Get(
        string id,
        CancellationToken cancellationToken)
    {
        TodoDto todo = await sender.Send(new GetTodoQuery(id), cancellationToken);
        return Ok(todo);
    }

    [HttpPut("{id}")]
    [ProducesResponseType<TodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TodoDto>> Update(
        string id,
        UpdateTodoRequest request,
        CancellationToken cancellationToken)
    {
        UpdateTodoCommand command = new UpdateTodoCommand(
            id,
            request.Name,
            request.Description,
            request.DueDate,
            request.Priority,
            request.Version);
        TodoDto todo = await sender.Send(command, cancellationToken);

        return Ok(todo);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        string id,
        DeleteTodoRequest request,
        CancellationToken cancellationToken)
    {
        _ = await sender.Send(
            new DeleteTodoCommand(id, request.Version),
            cancellationToken);

        return NoContent();
    }

    [HttpPost("{id}/restore")]
    [ProducesResponseType<TodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TodoDto>> Restore(
        string id,
        RestoreTodoRequest request,
        CancellationToken cancellationToken)
    {
        TodoDto todo = await sender.Send(
            new RestoreTodoCommand(id, request.Version),
            cancellationToken);

        return Ok(todo);
    }
}
