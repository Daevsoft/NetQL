using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Daevsoft.Lib
{
    public partial class DbUtils
    {
        private IBulkCopyAdapter bulkCopyAdapter;

        public DbUtils UseBulkCopyAdapter(IBulkCopyAdapter adapter)
        {
            EnsureAvailable();
            bulkCopyAdapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            return this;
        }

        public DbUtils UseDefaultBulkCopyAdapter()
        {
            EnsureAvailable();
            bulkCopyAdapter = null;
            return this;
        }

        public long BulkCopy(string tableName, object data, int batchSize = 5000, bool transactional = false)
        {
            EnsureAvailable();
            var rows = PrepareBulkCopy(tableName, data, batchSize, out var columns, out var table, out var adapter, out int size);
            if (adapter == null) return BulkCopyFallback(tableName, rows, size, transactional);

            EnterOperation();
            Exception failure = null;
            bool manageTransaction = false;
            try
            {
                manageTransaction = transactional && !HasTransaction;
                if (manageTransaction) useTransaction = true;
                OpenConnection();
                long total = adapter.Write(CreateBulkCopyContext(table, columns, size), rows);
                if (manageTransaction) EndTransaction(true);
                return total;
            }
            catch (Exception error)
            {
                failure = error;
                if (manageTransaction)
                {
                    try { EndTransaction(false); }
                    catch (Exception cleanup) { error.Data["NetQL.RollbackError"] = cleanup; }
                }
                throw;
            }
            finally
            {
                Clear();
                try { Cleanup(failure, () => { if (transaction == null) CloseConnection(); }); }
                finally { Volatile.Write(ref executing, 0); }
            }
        }

        public async Task<long> BulkCopyAsync(string tableName, object data, int batchSize = 5000, bool transactional = false,
            CancellationToken cancellationToken = default)
        {
            EnsureAvailable();
            var rows = PrepareBulkCopy(tableName, data, batchSize, out var columns, out var table, out var adapter, out int size);
            if (adapter == null)
                return await BulkCopyFallbackAsync(tableName, rows, size, transactional, cancellationToken).ConfigureAwait(false);

            EnterOperation();
            Exception failure = null;
            bool manageTransaction = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                manageTransaction = transactional && !HasTransaction;
                if (manageTransaction) useTransaction = true;
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                long total = await adapter.WriteAsync(CreateBulkCopyContext(table, columns, size), rows, cancellationToken).ConfigureAwait(false);
                if (manageTransaction) await EndTransactionAsync(true, cancellationToken).ConfigureAwait(false);
                return total;
            }
            catch (Exception error)
            {
                failure = error;
                if (manageTransaction)
                {
                    try { await EndTransactionAsync(false, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception cleanup) { error.Data["NetQL.RollbackError"] = cleanup; }
                }
                throw;
            }
            finally
            {
                Clear();
                try
                {
                    await CleanupAsync(failure,
                        () => transaction == null ? CloseConnectionAsync() : Task.CompletedTask).ConfigureAwait(false);
                }
                finally { Volatile.Write(ref executing, 0); }
            }
        }

        private List<object> PrepareBulkCopy(string tableName, object data, int rowBatchSize,
            out IReadOnlyList<BulkCopyColumn> columns, out string table, out IBulkCopyAdapter adapter, out int size)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("A table is required.", nameof(tableName));
            var rows = MaterializeBulkCopyRows(data);
            if (rows.Count == 0)
                throw new ArgumentException("Bulk copy data must contain at least one row.", nameof(data));
            columns = BulkCopyMapping.Resolve(rows);
            adapter = ResolveBulkCopyAdapter();
            size = rowBatchSize <= 0 ? 5000 : rowBatchSize;
            table = WrapTable(tableName, providerName == "Oracle");
            return rows;
        }

        private static List<object> MaterializeBulkCopyRows(object data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data is string)
                throw new ArgumentException("Bulk copy data must be an object or a sequence of objects.", nameof(data));
            var rows = new List<object>();
            if (data is IEnumerable items)
            {
                foreach (var item in items)
                {
                    if (item == null) throw new ArgumentException("Bulk copy rows must not be null.", nameof(data));
                    rows.Add(item);
                }
                return rows;
            }
            rows.Add(data);
            return rows;
        }

        private IBulkCopyAdapter ResolveBulkCopyAdapter()
        {
            if (bulkCopyAdapter != null) return bulkCopyAdapter;
            switch (providerName)
            {
                case "SqlServer": return SqlServerBulkCopyAdapter.Instance;
                case "PostgreSQL": return PostgreSqlBulkCopyAdapter.Instance;
                case "MySQL":
                    return connection.GetType().FullName == "MySqlConnector.MySqlConnection"
                        ? (IBulkCopyAdapter)MySqlBulkCopyAdapter.Instance : null;
                case "Oracle": return OracleBulkCopyAdapter.Instance;
                default: return null;
            }
        }

        private BulkCopyContext CreateBulkCopyContext(string table, IReadOnlyList<BulkCopyColumn> columns, int size)
        {
            return new BulkCopyContext(table, columns, size, commandTimeout ?? 0, connection, transaction, QuoteIdentifierPart);
        }

        private long BulkCopyFallback(string tableName, List<object> rows, int batchSize, bool transactional)
        {
            bool manageTransaction = transactional && !HasTransaction;
            if (manageTransaction) useTransaction = true;
            try
            {
                long total = Insert(tableName).BulkRows(rows, batchSize, !HasTransaction).ExecuteNonQuery();
                if (manageTransaction) Commit();
                return total;
            }
            catch (Exception error)
            {
                if (manageTransaction)
                {
                    try { Rollback(); }
                    catch (Exception cleanup) { error.Data["NetQL.RollbackError"] = cleanup; }
                }
                throw;
            }
        }

        private async Task<long> BulkCopyFallbackAsync(string tableName, List<object> rows, int batchSize, bool transactional,
            CancellationToken cancellationToken)
        {
            bool manageTransaction = transactional && !HasTransaction;
            if (manageTransaction) useTransaction = true;
            try
            {
                long total = await Insert(tableName).BulkRows(rows, batchSize, !HasTransaction)
                    .ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (manageTransaction) await CommitAsync(cancellationToken).ConfigureAwait(false);
                return total;
            }
            catch (Exception error)
            {
                if (manageTransaction)
                {
                    try { await RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception cleanup) { error.Data["NetQL.RollbackError"] = cleanup; }
                }
                throw;
            }
        }

        private async Task OpenConnectionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!(connection is DbConnection dbConnection))
                throw new NotSupportedException("Async operations require a DbConnection provider.");
            if (transaction != null && (transaction.Connection == null || connection.State != ConnectionState.Open))
                throw new InvalidOperationException("The attached transaction has completed or its connection is closed.");
            if (connection.State != ConnectionState.Open)
                await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (useTransaction && transaction == null)
            {
                transaction = await dbConnection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
                ownsTransaction = true;
            }
        }
    }
}
