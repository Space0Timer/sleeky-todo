namespace Sleeky.Todo.Application.Abstractions.Persistence;

public interface ITodoTransaction
{
    Task<TResult> ExecuteAsync<TResult>(
        Guid todoId,
        long expectedVersion,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
