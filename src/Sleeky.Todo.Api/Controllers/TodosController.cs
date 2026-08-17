using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Sleeky.Todo.Api.Contracts.Todos;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Todos.Commands.AddDependency;
using Sleeky.Todo.Application.Todos.Commands.Bulk;
using Sleeky.Todo.Application.Todos.Commands.Bulk.ChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Commands.Bulk.DeleteTodos;
using Sleeky.Todo.Application.Todos.Commands.Bulk.RestoreTodos;
using Sleeky.Todo.Application.Todos.Commands.ChangeTodoStatus;
using Sleeky.Todo.Application.Todos.Commands.CreateTodo;
using Sleeky.Todo.Application.Todos.Commands.DeleteTodo;
using Sleeky.Todo.Application.Todos.Commands.RemoveDependency;
using Sleeky.Todo.Application.Todos.Commands.RestoreTodo;
using Sleeky.Todo.Application.Todos.Commands.UpdateTodo;
using Sleeky.Todo.Application.Todos.Queries.GetTodo;
using Sleeky.Todo.Application.Todos.Queries.GetTodos;
using Sleeky.Todo.Application.Todos.Queries.GetTodoSelection;

namespace Sleeky.Todo.Api.Controllers;

/// <summary>
/// The TODO routes, each nested under the Space it acts in.
/// </summary>
/// <remarks>
/// The Space identifier is part of every route rather than a header or a
/// body field, so a request cannot name a TODO without also naming the Space
/// it expects to find it in. The controller only forwards it: the pipeline
/// behavior authorizes the caller against the Space before any handler runs,
/// and persistence then confines every read and write to that Space. A TODO
/// that lives in another Space — even one the caller is a member of — is
/// therefore a 404 on this route, not a leak.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/spaces/{spaceId:guid}/todos")]
public sealed class TodosController : ControllerBase
{
    private readonly ISender sender;

    public TodosController(ISender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);

        this.sender = sender;
    }

    [HttpGet]
    [ProducesResponseType<CursorPage<TodoListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CursorPage<TodoListItemDto>>> List(
        Guid spaceId,
        [FromQuery] GetTodosRequest request,
        CancellationToken cancellationToken)
    {
        GetTodosQuery query = new GetTodosQuery(
            spaceId,
            request.Status,
            request.Priority,
            request.DueFrom,
            request.DueTo,
            request.DependencyStatus,
            request.Scope,
            request.SortField,
            request.SortDirection,
            request.Limit,
            request.Cursor,
            request.Search);
        CursorPage<TodoListItemDto> page = await sender.Send(query, cancellationToken);

        return Ok(page);
    }

    [HttpPost]
    [ProducesResponseType<TodoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TodoDto>> Create(
        Guid spaceId,
        CreateTodoRequest request,
        CancellationToken cancellationToken)
    {
        CreateTodoCommand command = new CreateTodoCommand(
            spaceId,
            request.Name,
            request.Description,
            request.DueDate,
            request.Priority,
            request.Recurrence?.Type,
            request.Recurrence?.Interval,
            request.Recurrence?.Unit);
        TodoDto todo = await sender.Send(command, cancellationToken);

        return CreatedAtAction(nameof(Get), new { spaceId, id = todo.Id }, todo);
    }

    /// <summary>
    /// Reports the current state of specific TODOs. Identifiers that no longer
    /// resolve are absent from the response rather than failing it, so a client
    /// holding a stale selection can discover what changed and what vanished.
    /// </summary>
    [HttpGet("selection")]
    [ProducesResponseType<TodoSelection>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TodoSelection>> GetSelection(
        Guid spaceId,
        [FromQuery(Name = "id")] Guid[] ids,
        CancellationToken cancellationToken)
    {
        TodoSelection selection = await sender.Send(
            new GetTodoSelectionQuery(spaceId, ids ?? Array.Empty<Guid>()),
            cancellationToken);
        return Ok(selection);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<TodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TodoDto>> Get(
        Guid spaceId,
        Guid id,
        CancellationToken cancellationToken)
    {
        TodoDto todo = await sender.Send(new GetTodoQuery(spaceId, id), cancellationToken);
        return Ok(todo);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<TodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TodoDto>> Update(
        Guid spaceId,
        Guid id,
        UpdateTodoRequest request,
        CancellationToken cancellationToken)
    {
        UpdateTodoCommand command = new UpdateTodoCommand(
            spaceId,
            id,
            request.Name,
            request.Description,
            request.DueDate,
            request.Priority,
            request.Version);
        TodoDto todo = await sender.Send(command, cancellationToken);

        return Ok(todo);
    }

    [HttpPost("{id:guid}/dependencies")]
    [ProducesResponseType<TodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TodoDto>> AddDependency(
        Guid spaceId,
        Guid id,
        AddDependencyRequest request,
        CancellationToken cancellationToken)
    {
        TodoDto todo = await sender.Send(
            new AddDependencyCommand(spaceId, id, request.DependencyId, request.Version),
            cancellationToken);

        return Ok(todo);
    }

    [HttpDelete("{id:guid}/dependencies/{dependencyId:guid}")]
    [ProducesResponseType<TodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TodoDto>> RemoveDependency(
        Guid spaceId,
        Guid id,
        Guid dependencyId,
        RemoveDependencyRequest request,
        CancellationToken cancellationToken)
    {
        TodoDto todo = await sender.Send(
            new RemoveDependencyCommand(spaceId, id, dependencyId, request.Version),
            cancellationToken);

        return Ok(todo);
    }

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType<TodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TodoDto>> ChangeStatus(
        Guid spaceId,
        Guid id,
        ChangeTodoStatusRequest request,
        CancellationToken cancellationToken)
    {
        TodoDto todo = await sender.Send(
            new ChangeTodoStatusCommand(spaceId, id, request.Status, request.Version),
            cancellationToken);

        return Ok(todo);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<TodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TodoDto>> Delete(
        Guid spaceId,
        Guid id,
        DeleteTodoRequest request,
        CancellationToken cancellationToken)
    {
        TodoDto todo = await sender.Send(
            new DeleteTodoCommand(spaceId, id, request.Version),
            cancellationToken);

        return Ok(todo);
    }

    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType<TodoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TodoDto>> Restore(
        Guid spaceId,
        Guid id,
        RestoreTodoRequest request,
        CancellationToken cancellationToken)
    {
        TodoDto todo = await sender.Send(
            new RestoreTodoCommand(spaceId, id, request.Version),
            cancellationToken);

        return Ok(todo);
    }

    [HttpPut("status")]
    [ProducesResponseType<BulkTodoResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BulkTodoResult>> ChangeStatuses(
        Guid spaceId,
        BulkChangeTodoStatusRequest request,
        CancellationToken cancellationToken)
    {
        BulkTodoResult result = await sender.Send(
            new BulkChangeTodoStatusCommand(spaceId, request.Status, ToSelection(request.Items)),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("restore")]
    [ProducesResponseType<BulkTodoResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BulkTodoResult>> RestoreMany(
        Guid spaceId,
        BulkRestoreTodosRequest request,
        CancellationToken cancellationToken)
    {
        BulkTodoResult result = await sender.Send(
            new BulkRestoreTodosCommand(spaceId, ToSelection(request.Items)),
            cancellationToken);

        return Ok(result);
    }

    [HttpDelete]
    [ProducesResponseType<BulkTodoResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BulkTodoResult>> DeleteMany(
        Guid spaceId,
        BulkDeleteTodosRequest request,
        CancellationToken cancellationToken)
    {
        BulkTodoResult result = await sender.Send(
            new BulkDeleteTodosCommand(spaceId, ToSelection(request.Items)),
            cancellationToken);

        return Ok(result);
    }

    private static IReadOnlyCollection<BulkTodoItemRequest> ToSelection(
        IReadOnlyCollection<BulkTodoSelectionItem> items)
    {
        return items
            .Select(item => new BulkTodoItemRequest(item.Id, item.Version))
            .ToArray();
    }
}
