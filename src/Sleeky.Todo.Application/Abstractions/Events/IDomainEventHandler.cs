using Sleeky.Todo.Domain.Events;

namespace Sleeky.Todo.Application.Abstractions.Events;

public interface IDomainEventHandler<in TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    Task HandleAsync(
        TDomainEvent domainEvent,
        CancellationToken cancellationToken = default);
}
