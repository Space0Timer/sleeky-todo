using Sleeky.Todo.Application.Abstractions.Events;
using Sleeky.Todo.Domain.Events;

namespace Sleeky.Todo.Application.Events;

public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IEnumerable<IDomainEventHandler<TodoCompletedDomainEvent>>
        todoCompletedHandlers;

    public DomainEventDispatcher(
        IEnumerable<IDomainEventHandler<TodoCompletedDomainEvent>> todoCompletedHandlers)
    {
        ArgumentNullException.ThrowIfNull(todoCompletedHandlers);

        this.todoCompletedHandlers = todoCompletedHandlers;
    }

    public async Task DispatchAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (IDomainEvent domainEvent in domainEvents)
        {
            switch (domainEvent)
            {
                case TodoCompletedDomainEvent todoCompleted:
                    foreach (IDomainEventHandler<TodoCompletedDomainEvent> handler
                        in todoCompletedHandlers)
                    {
                        await handler.HandleAsync(todoCompleted, cancellationToken);
                    }

                    break;
                default:
                    throw new InvalidOperationException(
                        $"No dispatcher is configured for {domainEvent.GetType().Name}.");
            }
        }
    }
}
