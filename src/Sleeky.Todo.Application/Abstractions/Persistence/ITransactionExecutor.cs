namespace Sleeky.Todo.Application.Abstractions.Persistence;

/// <summary>
/// Runs a set of persistence operations as a single atomic unit. Repository
/// calls made inside the operation join the same transaction, which commits
/// when the operation returns and rolls back when it throws.
/// </summary>
public interface ITransactionExecutor
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
