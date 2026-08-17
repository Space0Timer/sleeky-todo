using MediatR;

using Sleeky.Todo.Application.DTOs;

namespace Sleeky.Todo.Application.Spaces.Commands.CreateSpace;

/// <summary>
/// Creates a Space with the current user as its only member, as Owner.
/// </summary>
/// <remarks>
/// Not Space-scoped: there is no Space to authorize against until this has
/// run, and any authenticated user may start one.
/// </remarks>
public sealed record CreateSpaceCommand(string Name) : IRequest<SpaceDto>;
