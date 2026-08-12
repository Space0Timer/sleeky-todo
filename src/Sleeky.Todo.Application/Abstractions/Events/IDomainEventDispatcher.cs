using Sleeky.Todo.Domain.Events;

namespace Sleeky.Todo.Application.Abstractions.Events;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default);
}
