using MongoDB.Driver;

using Sleeky.Todo.Application.Abstractions.Persistence;
using Sleeky.Todo.Application.Exceptions;

namespace Sleeky.Todo.Infrastructure.Persistence.Transactions;

internal sealed class MongoTodoTransaction : ITodoTransaction
{
    private readonly IMongoClient mongoClient;
    private readonly MongoTransactionContext transactionContext;

    public MongoTodoTransaction(
        IMongoClient mongoClient,
        MongoTransactionContext transactionContext)
    {
        ArgumentNullException.ThrowIfNull(mongoClient);
        ArgumentNullException.ThrowIfNull(transactionContext);

        this.mongoClient = mongoClient;
        this.transactionContext = transactionContext;
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Guid todoId,
        long expectedVersion,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using IClientSessionHandle session = await mongoClient.StartSessionAsync(
            cancellationToken: cancellationToken);
        transactionContext.Session = session;

        try
        {
            session.StartTransaction();
            TResult result = await operation(cancellationToken);
            await session.CommitTransactionAsync(cancellationToken);
            return result;
        }
        catch (MongoException exception) when (IsConcurrencyConflict(exception))
        {
            await AbortIfActiveAsync(session);
            throw new ConcurrencyConflictException("TODO", todoId, expectedVersion);
        }
        catch
        {
            await AbortIfActiveAsync(session);
            throw;
        }
        finally
        {
            transactionContext.Session = null;
        }
    }

    private static bool IsConcurrencyConflict(MongoException exception)
    {
        return exception.HasErrorLabel("TransientTransactionError")
            || (exception is MongoWriteException writeException
                && writeException.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            || (exception is MongoCommandException commandException
                && commandException.Code is 11000 or 112);
    }

    private static async Task AbortIfActiveAsync(IClientSessionHandle session)
    {
        if (session.IsInTransaction)
        {
            await session.AbortTransactionAsync(CancellationToken.None);
        }
    }
}
