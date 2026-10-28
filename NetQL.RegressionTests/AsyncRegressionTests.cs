using System.Data;
using Daevsoft;
using Daevsoft.Lib;
using Microsoft.Data.Sqlite;

namespace Daevsoft.RegressionTests;

[TestClass]
public class AsyncRegressionTests
{
    [TestMethod]
    public async Task AsyncCrudAndTransactionsUseGeneratedFilters()
    {
        await using var db = new Daevsoft.NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        await db.Query("CREATE TABLE items(id INTEGER PRIMARY KEY, name TEXT)").ExecuteAsync();
        db.Transaction();
        Assert.AreEqual(1, await db.Insert("items").AddValue("id", 1).AddValue("name", "one").ExecuteAsync());
        Assert.AreEqual("one", await db.Select("name", "items").Where("id", 1).ReadSingleAsync<string>());
        await db.RollbackAsync();
        Assert.IsFalse(await db.Select("items").IsExistAsync());
        db.Transaction();
        await db.Insert("items").AddValue("id", 2).AddValue("name", "two").ExecuteAsync();
        await db.CommitAsync();
        Assert.AreEqual(2, (await db.Select("items").ReadAsAsync<Item>()).Id);
        Assert.AreEqual(1, (await db.Select("items").ReadAsListAsync<Item>()).Count);
    }

    [TestMethod]
    public async Task CancelledOperationClosesAndClearsSession()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = new Daevsoft.NetQL(connection);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => db.Query("SELECT 1").ReadSingleAsync<int>(cancellation.Token));
        Assert.AreEqual(ConnectionState.Closed, connection.State);
        Assert.AreEqual(42, await db.Query("SELECT 42").ReadSingleAsync<int>());
    }

    [TestMethod]
    public async Task StreamingAwaitsCallbacksAndRejectsConcurrentSessionMutation()
    {
        await using var db = new Daevsoft.NetQL(new SqliteConnection("Data Source=:memory:"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int visits = 0;
        var read = db.Query("SELECT 1 UNION ALL SELECT 2").ReadAsync(async (reader, token) =>
        {
            visits++;
            if (visits == 1)
            {
                Assert.AreEqual(1, reader.GetValue<int>(0));
                entered.SetResult();
                await resume.Task.WaitAsync(token);
            }
        });
        await entered.Task;
        try
        {
            Assert.AreEqual(1, visits);
            Assert.ThrowsExactly<InvalidOperationException>(() => db.Query("SELECT 99"));
            Assert.ThrowsExactly<InvalidOperationException>(() => db.Where("id", 99));
            Assert.ThrowsExactly<InvalidOperationException>(() => db.Commit());
            Assert.ThrowsExactly<InvalidOperationException>(() => db.Dispose());
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.ReadSingleAsync<int>());
        }
        finally { resume.TrySetResult(); }
        Assert.AreEqual(2, await read);
        Assert.AreEqual(3, await db.Query("SELECT 3").ReadSingleAsync<int>());
    }

    [TestMethod]
    public async Task CallbackCancellationRollsBackOwnedTransaction()
    {
        await using var db = new Daevsoft.NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        await db.Query("CREATE TABLE items(id INTEGER)").ExecuteAsync();
        db.Transaction();
        await db.Query("INSERT INTO items VALUES (1)").ExecuteAsync();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => db.Select("items").ReadAsync((reader, token) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token));
        Assert.AreEqual(0, await db.Query("SELECT COUNT(*) FROM items").ReadSingleAsync<int>());
        await db.RollbackAsync();
    }

    [TestMethod]
    public async Task CallerOwnedConnectionAndTransactionAreNotCompletedOrDisposed()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        await using (var db = new Daevsoft.NetQL(connection, Provider.SQLite, leaveOpen: true))
        {
            db.Transaction(transaction);
            Assert.AreEqual(7, await db.Query("SELECT 7").ReadSingleAsync<int>());
            Assert.ThrowsExactly<InvalidOperationException>(() => db.Commit());
        }
        Assert.AreEqual(ConnectionState.Open, connection.State);
        transaction.Rollback();
    }

    [TestMethod]
    public async Task DisposingPendingTransactionRollsBackButKeepsBorrowedConnectionOpen()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE items(id INTEGER)";
            setup.ExecuteNonQuery();
        }
        await using (var db = new Daevsoft.NetQL(connection, Provider.SQLite, leaveOpen: true))
        {
            db.Transaction();
            await db.Query("INSERT INTO items VALUES (1)").ExecuteAsync();
        }
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM items";
        Assert.AreEqual(0L, check.ExecuteScalar());
    }

    public sealed class Item
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
