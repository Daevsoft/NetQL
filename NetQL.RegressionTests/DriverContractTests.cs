using System.Data;
using System.Data.Common;
using Daevsoft.Lib;
using Microsoft.Data.Sqlite;

namespace Daevsoft.RegressionTests;

[TestClass]
public class DriverContractTests
{
    [TestMethod]
    public async Task AsyncOperationsUseProviderAsyncMethodsAndEnlistTransaction()
    {
        using var cancellation = new CancellationTokenSource();
        var connection = new AsyncConnection(cancellation.Token);
        await using var db = new NetQL(connection, Provider.SQLite);
        db.CommandTimeout(17).Transaction();
        Assert.AreEqual(1, await db.Query("UPDATE items SET name = @name")
            .AddParameter("name", "safe").ExecuteAsync(cancellation.Token));
        await db.CommitAsync(cancellation.Token);
        Assert.AreEqual(ConnectionState.Closed, connection.State);
        Assert.AreEqual(1, connection.CommandsDisposed);
        Assert.AreEqual(1, connection.TransactionsDisposed);
        Assert.IsTrue(connection.Committed);
    }

    [TestMethod]
    public async Task ProviderFailureSurvivesRollbackFailureAndResourcesAreReleased()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new InvalidOperationException("command failure");
        var connection = new AsyncConnection(cancellation.Token) { Failure = expected, FailRollback = true };
        await using var db = new NetQL(connection, Provider.SQLite);
        db.Transaction();
        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.Query("DELETE FROM items").ExecuteAsync(cancellation.Token));
        Assert.AreSame(expected, actual);
        Assert.IsTrue(actual.Data.Contains("NetQL.RollbackError"));
        Assert.AreEqual(ConnectionState.Closed, connection.State);
        Assert.AreEqual(1, connection.CommandsDisposed);
        Assert.AreEqual(1, connection.TransactionsDisposed);
    }

    private sealed class AsyncConnection(CancellationToken expectedToken) : DbConnection
    {
        private ConnectionState state;
        public Exception? Failure { get; set; }
        public bool FailRollback { get; set; }
        public int CommandsDisposed { get; set; }
        public int TransactionsDisposed { get; set; }
        public bool Committed { get; set; }
        public override string ConnectionString { get; set; } = "";
        public override string Database => "contract-test";
        public override string DataSource => "contract-test";
        public override string ServerVersion => "1";
        public override ConnectionState State => state;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open() => throw new InvalidOperationException("Synchronous open is forbidden in this test.");
        public override void Close() => state = ConnectionState.Closed;
        public override Task CloseAsync() { Close(); return Task.CompletedTask; }
        public override Task OpenAsync(CancellationToken token)
        {
            Assert.AreEqual(expectedToken, token);
            state = ConnectionState.Open;
            return Task.CompletedTask;
        }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new InvalidOperationException("Synchronous begin is forbidden in this test.");
        protected override ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken token)
        {
            Assert.AreEqual(expectedToken, token);
            return ValueTask.FromResult<DbTransaction>(new AsyncTransaction(this, expectedToken));
        }
        protected override DbCommand CreateDbCommand() => new AsyncCommand(this, expectedToken);
    }

    private sealed class AsyncTransaction(AsyncConnection owner, CancellationToken expectedToken) : DbTransaction
    {
        protected override DbConnection DbConnection => owner;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public override void Commit() => throw new InvalidOperationException("Synchronous commit is forbidden in this test.");
        public override void Rollback() => throw new InvalidOperationException("Synchronous rollback is forbidden in this test.");
        public override Task CommitAsync(CancellationToken token = default)
        {
            Assert.AreEqual(expectedToken, token);
            owner.Committed = true;
            return Task.CompletedTask;
        }
        public override Task RollbackAsync(CancellationToken token = default)
        {
            Assert.AreEqual(CancellationToken.None, token);
            if (owner.FailRollback) throw new InvalidOperationException("rollback failure");
            return Task.CompletedTask;
        }
        public override ValueTask DisposeAsync()
        {
            owner.TransactionsDisposed++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncCommand(AsyncConnection owner, CancellationToken expectedToken) : DbCommand
    {
        private readonly SqliteCommand parameters = new();
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection DbConnection { get; set; } = owner;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery() => throw new InvalidOperationException("Synchronous execute is forbidden in this test.");
        public override object ExecuteScalar() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        protected override DbParameter CreateDbParameter() => parameters.CreateParameter();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken token)
        {
            Assert.AreEqual(expectedToken, token);
            Assert.AreSame(owner, DbTransaction?.Connection);
            if (owner.Failure != null) return Task.FromException<int>(owner.Failure);
            Assert.AreEqual(17, CommandTimeout);
            Assert.AreEqual("safe", parameters.Parameters["name"].Value);
            return Task.FromResult(1);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) parameters.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync()
        {
            owner.CommandsDisposed++;
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
