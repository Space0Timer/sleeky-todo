using Sleeky.Todo.Application.Abstractions.Persistence;

namespace Sleeky.Todo.Application.Tests.Todos.Commands.ChangeTodoStatus;

internal sealed class CompletingTodoTransaction : ITodoTransaction
{
    public bool Completed { get; private set; }

    public async Task<TResult> ExecuteAsync<TResult>(
        Guid todoId,
        long expectedVersion,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        TResult result = await operation(cancellationToken);
        Completed = true;
        return result;
    }
}
