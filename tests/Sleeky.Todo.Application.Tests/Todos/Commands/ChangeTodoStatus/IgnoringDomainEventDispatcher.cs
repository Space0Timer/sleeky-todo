using Sleeky.Todo.Application.Abstractions.Events;
using Sleeky.Todo.Domain.Events;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.ChangeTodoStatus;

internal sealed class IgnoringDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
