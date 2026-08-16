using MediatR;

namespace Sleeky.Todo.Application.Spaces.Access;

/// <summary>
/// Authorizes every <see cref="ISpaceScopedRequest"/> before its handler
/// runs, and lets every other request through untouched.
/// </summary>
/// <remarks>
/// This is where Space access is enforced — not in handlers. Registered after
/// validation, so a request that fails to name a Space is a validation error
/// rather than a lookup of the empty identifier, and before the handler, so a
/// handler that runs can rely on <see cref="Abstractions.Identity.ISpaceScope"/>
/// being bound. Because it is a pipeline behavior there is no handler that
/// can omit it; the only way to bypass it is to not implement the marker
/// interface, and then the repository refuses the unbound scope.
/// </remarks>
public sealed class SpaceAccessBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ISpaceAccessService accessService;

    public SpaceAccessBehavior(ISpaceAccessService accessService)
    {
        ArgumentNullException.ThrowIfNull(accessService);

        this.accessService = accessService;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is ISpaceScopedRequest scopedRequest)
        {
            await accessService.RequireAsync(
                scopedRequest.SpaceId,
                scopedRequest.RequiredPermission,
                cancellationToken);
        }

        return await next(cancellationToken);
    }
}
