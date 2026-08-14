using Sleeky.Todo.Application.Abstractions.Persistence;

namespace Sleeky.Todo.Application.Tests;

internal sealed class ImmediateTransactionExecutor : ITransactionExecutor
{
    public int ExecutionCount { get; private set; }

    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return operation(cancellationToken);
    }
}
