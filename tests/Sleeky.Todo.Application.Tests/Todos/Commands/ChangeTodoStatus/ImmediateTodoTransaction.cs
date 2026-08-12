using Sleeky.Todo.Application.Abstractions.Persistence;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.ChangeTodoStatus;

internal sealed class ImmediateTodoTransaction : ITodoTransaction
{
    public Task<TResult> ExecuteAsync<TResult>(
        string todoId,
        long expectedVersion,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        return operation(cancellationToken);
    }
}
