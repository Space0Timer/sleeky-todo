using FluentAssertions;

using MediatR;

using NSubstitute;

using Sleeky.Todo.Application.Exceptions;
using Sleeky.Todo.Application.Spaces.Access;
using Sleeky.Todo.Domain.Enums;

namespace Sleeky.Todo.Application.Tests.Spaces.Access;

[TestClass]
public sealed class SpaceAccessBehaviorTests
{
    private static readonly Guid SpaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public async Task ARequestThatIsNotSpaceScopedPassesThroughWithoutACheck()
    {
        ISpaceAccessService accessService = Substitute.For<ISpaceAccessService>();
        SpaceAccessBehavior<UnscopedRequest, string> behavior =
            new SpaceAccessBehavior<UnscopedRequest, string>(accessService);

        string result = await behavior.Handle(
            new UnscopedRequest(),
            _ => Task.FromResult("handled"),
            CancellationToken.None);

        result.Should().Be("handled");
        await accessService.DidNotReceiveWithAnyArgs().RequireAsync(default, default, default);
    }

    [TestMethod]
    public async Task AScopedRequestIsCheckedAtItsRequiredLevelBeforeTheHandlerRuns()
    {
        ISpaceAccessService accessService = Substitute.For<ISpaceAccessService>();
        SpaceAccessBehavior<ScopedRequest, string> behavior =
            new SpaceAccessBehavior<ScopedRequest, string>(accessService);
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        List<string> order = new List<string>();
        accessService
            .RequireAsync(SpaceId, SpacePermission.Write, cancellation.Token)
            .Returns(_ =>
            {
                order.Add("check");
                return new SpaceAccessContext(SpaceId, "Project Alpha", SpacePermission.Owner);
            });

        string result = await behavior.Handle(
            new ScopedRequest(SpaceId, SpacePermission.Write),
            _ =>
            {
                order.Add("handler");
                return Task.FromResult("handled");
            },
            cancellation.Token);

        result.Should().Be("handled");
        order.Should().Equal("check", "handler");
    }

    [TestMethod]
    public async Task ARefusedCheckStopsTheHandlerFromRunning()
    {
        ISpaceAccessService accessService = Substitute.For<ISpaceAccessService>();
        accessService
            .RequireAsync(SpaceId, SpacePermission.Read, Arg.Any<CancellationToken>())
            .Returns<SpaceAccessContext>(_ => throw new NotFoundException("Space", SpaceId));
        SpaceAccessBehavior<ScopedRequest, string> behavior =
            new SpaceAccessBehavior<ScopedRequest, string>(accessService);
        bool handlerRan = false;

        Func<Task> act = () => behavior.Handle(
            new ScopedRequest(SpaceId, SpacePermission.Read),
            _ =>
            {
                handlerRan = true;
                return Task.FromResult("handled");
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        handlerRan.Should().BeFalse();
    }

    private sealed record UnscopedRequest : IRequest<string>;

    private sealed record ScopedRequest(Guid SpaceId, SpacePermission RequiredPermission)
        : IRequest<string>, ISpaceScopedRequest;
}
