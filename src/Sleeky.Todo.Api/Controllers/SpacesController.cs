using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Sleeky.Todo.Api.Contracts.Spaces;
using Sleeky.Todo.Application.DTOs;
using Sleeky.Todo.Application.Spaces.Commands.AddSpaceAccess;
using Sleeky.Todo.Application.Spaces.Commands.ChangeSpacePermission;
using Sleeky.Todo.Application.Spaces.Commands.CreateSpace;
using Sleeky.Todo.Application.Spaces.Commands.RemoveSpaceAccess;
using Sleeky.Todo.Application.Spaces.Commands.RenameSpace;
using Sleeky.Todo.Application.Spaces.Queries.GetSpace;
using Sleeky.Todo.Application.Spaces.Queries.GetSpaces;

namespace Sleeky.Todo.Api.Controllers;

/// <summary>
/// The Spaces a user belongs to, and who else belongs to each.
/// </summary>
/// <remarks>
/// A Space the caller is not a member of is answered 404 on every route
/// here, the same as an identifier that does not exist; a member below the
/// level a route needs is answered 403. Neither decision is made in this
/// controller: each request names its Space and the level it needs, and the
/// application pipeline enforces both before a handler runs.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/spaces")]
public sealed class SpacesController : ControllerBase
{
    private readonly ISender sender;

    public SpacesController(ISender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);

        this.sender = sender;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SpaceSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SpaceSummaryDto>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SpaceSummaryDto> spaces = await sender.Send(
            new GetSpacesQuery(),
            cancellationToken);

        return Ok(spaces);
    }

    [HttpPost]
    [ProducesResponseType<SpaceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SpaceDto>> Create(
        CreateSpaceRequest request,
        CancellationToken cancellationToken)
    {
        SpaceDto space = await sender.Send(
            new CreateSpaceCommand(request.Name),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { spaceId = space.Id }, space);
    }

    [HttpGet("{spaceId:guid}")]
    [ProducesResponseType<SpaceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SpaceDto>> Get(
        Guid spaceId,
        CancellationToken cancellationToken)
    {
        SpaceDto space = await sender.Send(new GetSpaceQuery(spaceId), cancellationToken);

        return Ok(space);
    }

    [HttpPut("{spaceId:guid}")]
    [ProducesResponseType<SpaceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SpaceDto>> Rename(
        Guid spaceId,
        RenameSpaceRequest request,
        CancellationToken cancellationToken)
    {
        SpaceDto space = await sender.Send(
            new RenameSpaceCommand(spaceId, request.Name, request.Version),
            cancellationToken);

        return Ok(space);
    }

    /// <summary>
    /// The Space's access list. Any member may read it, so members can see
    /// who else shares the Space and at what level.
    /// </summary>
    [HttpGet("{spaceId:guid}/access")]
    [ProducesResponseType<IReadOnlyCollection<SpaceAccessDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyCollection<SpaceAccessDto>>> ListAccess(
        Guid spaceId,
        CancellationToken cancellationToken)
    {
        SpaceDto space = await sender.Send(new GetSpaceQuery(spaceId), cancellationToken);

        return Ok(space.Access);
    }

    [HttpPost("{spaceId:guid}/access")]
    [ProducesResponseType<SpaceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SpaceDto>> AddAccess(
        Guid spaceId,
        AddSpaceAccessRequest request,
        CancellationToken cancellationToken)
    {
        SpaceDto space = await sender.Send(
            new AddSpaceAccessCommand(
                spaceId,
                request.SubjectId,
                request.Permission,
                request.Version),
            cancellationToken);

        return Ok(space);
    }

    [HttpPut("{spaceId:guid}/access/{subjectId:guid}")]
    [ProducesResponseType<SpaceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SpaceDto>> ChangePermission(
        Guid spaceId,
        Guid subjectId,
        ChangeSpacePermissionRequest request,
        CancellationToken cancellationToken)
    {
        SpaceDto space = await sender.Send(
            new ChangeSpacePermissionCommand(
                spaceId,
                subjectId,
                request.Permission,
                request.Version),
            cancellationToken);

        return Ok(space);
    }

    [HttpDelete("{spaceId:guid}/access/{subjectId:guid}")]
    [ProducesResponseType<SpaceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SpaceDto>> RemoveAccess(
        Guid spaceId,
        Guid subjectId,
        RemoveSpaceAccessRequest request,
        CancellationToken cancellationToken)
    {
        SpaceDto space = await sender.Send(
            new RemoveSpaceAccessCommand(spaceId, subjectId, request.Version),
            cancellationToken);

        return Ok(space);
    }
}
