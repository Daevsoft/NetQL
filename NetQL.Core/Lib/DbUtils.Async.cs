using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Daevsoft.Lib
{
    public partial class DbUtils
    {
        public Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            return StartScopeAsync(cmd => cmd.ExecuteNonQueryAsync(cancellationToken), cancellationToken);
        }

        public Task<int> ReadAsync(Func<ReaderUtil<IDataReader>, CancellationToken, Task> callback, CancellationToken cancellationToken = default)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return ReadRowsAsync(callback, false, cancellationToken);
        }

        private Task<int> ReadRowsAsync(Func<ReaderUtil<IDataReader>, CancellationToken, Task> callback, bool firstOnly, CancellationToken cancellationToken)
        {
            return StartScopeAsync(async cmd =>
            {
                int rows = 0;
                var dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                Exception failure = null;
                try
                {
                    var reader = new ReaderUtil<IDataReader>(dataReader);
                    while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await callback(reader, cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        rows++;
                        if (firstOnly) break;
                    }
                }
                catch (Exception error) { failure = error; throw; }
                finally { await CleanupAsync(failure, () => dataReader.DisposeAsync().AsTask()).ConfigureAwait(false); }
                return rows;
            }, cancellationToken);
        }

        public async Task<A> ReadSingleAsync<A>(CancellationToken cancellationToken = default)
        {
            A value = default;
            await ReadRowsAsync((reader, token) =>
            {
                value = reader.GetValue<A>(0);
                return Task.CompletedTask;
            }, true, cancellationToken).ConfigureAwait(false);
            return value;
        }

        public async Task<A> ReadAsAsync<A>(CancellationToken cancellationToken = default)
        {
            A value = default;
            await ReadRowsAsync((reader, token) =>
            {
                value = MapRow<A>(reader);
                return Task.CompletedTask;
            }, true, cancellationToken).ConfigureAwait(false);
            return value;
        }

        public async Task<List<A>> ReadAsListAsync<A>(CancellationToken cancellationToken = default)
        {
            var values = new List<A>();
            await ReadAsync((reader, token) =>
            {
                values.Add(MapRow<A>(reader));
                return Task.CompletedTask;
            }, cancellationToken).ConfigureAwait(false);
            return values;
        }

        public async Task<bool> IsExistAsync(CancellationToken cancellationToken = default)
        {
            return await ReadRowsAsync((reader, token) => Task.CompletedTask, true, cancellationToken).ConfigureAwait(false) != 0;
        }

        private async Task<T> StartScopeAsync<T>(Func<DbCommand, Task<T>> operation, CancellationToken cancellationToken)
        {
            EnterOperation();
            Exception failure = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!(connection is DbConnection dbConnection))
                    throw new NotSupportedException("Async operations require a DbConnection provider.");
                PrepareCommand();
                if (!(command is DbCommand dbCommand))
                    throw new NotSupportedException("Async operations require a DbCommand provider.");
                if (transaction != null && (transaction.Connection == null || connection.State != ConnectionState.Open))
                    throw new InvalidOperationException("The attached transaction has completed or its connection is closed.");
                if (connection.State != ConnectionState.Open)
                    await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                if (useTransaction && transaction == null)
                {
                    transaction = await dbConnection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
                    ownsTransaction = true;
                }
                dbCommand.Transaction = (DbTransaction)transaction;
                return await operation(dbCommand).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failure = error;
                if (ownsTransaction)
                {
                    try { await EndTransactionAsync(false, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception cleanup) { error.Data["NetQL.RollbackError"] = cleanup; }
                }
                throw;
            }
            finally
            {
                var current = command;
                command = null;
                Clear();
                try
                {
                    await CleanupAsync(failure,
                        () => DisposeResourceAsync(current),
                        () => transaction == null ? CloseConnectionAsync() : Task.CompletedTask).ConfigureAwait(false);
                }
                finally { Volatile.Write(ref executing, 0); }
            }
        }

        private static Task DisposeResourceAsync(IDisposable resource)
        {
            if (resource is IAsyncDisposable asyncResource) return asyncResource.DisposeAsync().AsTask();
            resource?.Dispose();
            return Task.CompletedTask;
        }

        private static async Task CleanupAsync(Exception original, params Func<Task>[] actions)
        {
            Exception failure = original;
            for (int i = 0; i < actions.Length; i++)
            {
                try { await actions[i]().ConfigureAwait(false); }
                catch (Exception cleanup)
                {
                    if (failure == null) failure = cleanup;
                    else failure.Data["NetQL.CleanupError." + i] = cleanup;
                }
            }
            if (original == null && failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            EnterOperation();
            try { await EndTransactionAsync(true, cancellationToken).ConfigureAwait(false); }
            finally { Volatile.Write(ref executing, 0); }
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            EnterOperation();
            try { await EndTransactionAsync(false, cancellationToken).ConfigureAwait(false); }
            finally { Volatile.Write(ref executing, 0); }
        }

        private async Task EndTransactionAsync(bool commit, CancellationToken cancellationToken)
        {
            if (transaction != null && !ownsTransaction)
                throw new InvalidOperationException("The caller owns this transaction and must complete it directly.");
            var current = transaction;
            transaction = null;
            ownsTransaction = false;
            useTransaction = false;
            isolationLevel = IsolationLevel.Unspecified;
            Exception failure = null;
            try
            {
                if (current is DbTransaction dbTransaction)
                {
                    if (commit) await dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    else if (current.Connection != null) await dbTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (current != null)
                    throw new NotSupportedException("Async transaction completion requires a DbTransaction provider.");
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                await CleanupAsync(failure, () => DisposeResourceAsync(current), CloseConnectionAsync).ConfigureAwait(false);
            }
        }

        private async Task CloseConnectionAsync()
        {
            if (parentDb != null || !ownsConnection || keepAliveConnection || connection.State == ConnectionState.Closed) return;
            if (connection is DbConnection dbConnection) await dbConnection.CloseAsync().ConfigureAwait(false);
            else connection.Close();
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            EnterOperation(true);
            Exception failure = null;
            try
            {
                if (ownsTransaction) await EndTransactionAsync(false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                transaction = null;
                useTransaction = false;
                Clear();
                disposed = true;
                try
                {
                    await CleanupAsync(failure, () => ownsConnection ? DisposeResourceAsync(connection) : Task.CompletedTask).ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref executing, 0);
                    GC.SuppressFinalize(this);
                }
            }
        }
    }
}
