using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Daevsoft.Lib;
using Microsoft.Data.Sqlite;

namespace Daevsoft.RegressionTests;

[TestClass]
public class BulkCopyRegressionTests
{
    [TestMethod]
    public void SqliteFallsBackToChunkedSql()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(Id INTEGER PRIMARY KEY, Text TEXT)").Execute();
        var rows = Enumerable.Range(1, 120).Select(i => new BulkRow { Id = i, Text = "row" + i }).ToList();
        Assert.AreEqual(120L, db.BulkCopy("items", rows, batchSize: 25));
        Assert.AreEqual(120L, db.Select("COUNT(*)", "items").ReadSingle<long>());
        Assert.AreEqual("row120", db.Select("Text", "items").Where("Id", 120).ReadSingle<string>());
    }

    [TestMethod]
    public void SqliteFallbackHonorsColumnMappingAndSkipsIgnoredProperties()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(identifier INTEGER PRIMARY KEY, Text TEXT)").Execute();
        var rows = new[] { new MappedBulkRow { Id = 1, Text = "first" }, new MappedBulkRow { Id = 2, Text = "second" } };
        Assert.AreEqual(2L, db.BulkCopy("items", rows));
        Assert.AreEqual("second", db.Select("Text", "items").Where("identifier", 2).ReadSingle<string>());
    }

    [TestMethod]
    public void SqliteFallbackRollsBackEveryChunkWhenTransactional()
    {
        using var db = OpenBulkTable(out var rows);
        Assert.ThrowsExactly<SqliteException>(() => db.BulkCopy("items", rows, batchSize: 10, transactional: true));
        Assert.AreEqual(0L, db.Select("COUNT(*)", "items").ReadSingle<long>());
    }

    [TestMethod]
    public void SqliteFallbackAutocommitsEachChunkByDefault()
    {
        using var db = OpenBulkTable(out var rows);
        Assert.ThrowsExactly<SqliteException>(() => db.BulkCopy("items", rows, batchSize: 10));
        Assert.AreEqual(10L, db.Select("COUNT(*)", "items").ReadSingle<long>());
    }

    [TestMethod]
    public void ConfiguredAdapterReceivesContextRowsAndTotal()
    {
        using var db = OpenBulkTable(out _);
        var rows = new[] { new BulkRow { Id = 1, Text = "one" }, new BulkRow { Id = 2, Text = "two" } };
        var adapter = new RecordingAdapter();
        db.UseBulkCopyAdapter(adapter);

        Assert.AreEqual(2L, db.BulkCopy("items", rows, batchSize: 1));

        Assert.AreEqual(1, adapter.SyncCalls);
        Assert.AreEqual(2, adapter.Rows.Count);
        Assert.AreEqual(1, adapter.Context.BatchSize);
        Assert.AreEqual("\"items\"", adapter.Context.TableName);
        CollectionAssert.AreEqual(new[] { "Id", "Text" }, adapter.Context.Columns.Select(column => column.ColumnName).ToArray());
        Assert.AreEqual(typeof(int), adapter.Context.Columns[0].ClrType);
        Assert.IsNull(adapter.Context.Transaction);
    }

    [TestMethod]
    public async Task AdapterAsyncReceivesCancellationToken()
    {
        using var db = OpenBulkTable(out _);
        var adapter = new RecordingAdapter();
        db.UseBulkCopyAdapter(adapter);
        using var cancellation = new CancellationTokenSource();
        var rows = new[] { new BulkRow { Id = 1, Text = "one" } };

        Assert.AreEqual(1L, await db.BulkCopyAsync("items", rows, batchSize: 100, cancellationToken: cancellation.Token));
        Assert.AreEqual(1, adapter.AsyncCalls);
        Assert.AreEqual(cancellation.Token, adapter.Token);
    }

    [TestMethod]
    public void TransactionalAdapterFailureRollsBackItsWrites()
    {
        using var db = OpenBulkTable(out _);
        db.UseBulkCopyAdapter(new InsertingAdapter { Fail = true });
        var rows = new[] { new BulkRow { Id = 500, Text = "rolled back" } };
        Assert.ThrowsExactly<InvalidOperationException>(() => db.BulkCopy("items", rows, transactional: true));
        Assert.AreEqual(0L, db.Select("COUNT(*)", "items").ReadSingle<long>());
    }

    [TestMethod]
    public void TransactionalAdapterCommitsWhenItSucceeds()
    {
        var connectionString = "Data Source=file:bulk_commit?mode=memory&cache=shared";
        using var keeper = new SqliteConnection(connectionString);
        keeper.Open();
        using var db = new NetQL(new SqliteConnection(connectionString), Provider.SQLite);
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(Id INTEGER PRIMARY KEY, Text TEXT)").Execute();
        db.UseBulkCopyAdapter(new InsertingAdapter());
        var rows = new[] { new BulkRow { Id = 501, Text = "committed" } };

        Assert.AreEqual(1L, db.BulkCopy("items", rows, transactional: true));

        using var verify = new SqliteConnection(connectionString);
        verify.Open();
        using var command = verify.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM items";
        Assert.AreEqual(1L, (long)command.ExecuteScalar());
    }

    [TestMethod]
    public void BulkCopyRejectsInvalidInput()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        Assert.ThrowsExactly<ArgumentException>(() => db.BulkCopy("", new BulkRow()));
        Assert.ThrowsExactly<ArgumentException>(() => db.BulkCopy("items", Array.Empty<BulkRow>()));
        Assert.ThrowsExactly<ArgumentException>(() => db.BulkCopy("items", new object[] { new BulkRow(), null! }));
        Assert.ThrowsExactly<ArgumentException>(() => db.BulkCopy("items", "not rows"));
        Assert.ThrowsExactly<ArgumentNullException>(() => db.BulkCopy("items", null!));
    }

    [TestMethod]
    public void SqlServerChunkingCapsBatchesToProviderLimits()
    {
        var connection = new CountingConnection();
        using var db = new NetQL(connection, Provider.SqlServer);
        var rows = Enumerable.Range(1, 2500).Select(i => new BulkRow { Id = i, Text = "row" }).ToList();
        Assert.AreEqual(3, db.Insert("items").Bulk(rows, 2000).ExecuteNonQuery());
        Assert.AreEqual(3, connection.Statements.Count);
        foreach (var statement in connection.Statements)
            Assert.IsTrue(Regex.Matches(statement, @"\),\(").Count + 1 <= 1000);
    }

    private static NetQL OpenBulkTable(out List<BulkRow> rows)
    {
        var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(Id INTEGER PRIMARY KEY, Text TEXT)").Execute();
        rows = Enumerable.Range(1, 20).Select(i => new BulkRow { Id = i == 19 ? 10 : i, Text = "row" }).ToList();
        return db;
    }

    public sealed class BulkRow
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
    }

    public sealed class MappedBulkRow
    {
        [Column("identifier")]
        public int Id { get; set; }
        public string Text { get; set; } = "";
        [NotMapped]
        public string Display => Text;
        public string _ignored { get; set; } = "";
    }

    private sealed class RecordingAdapter : IBulkCopyAdapter
    {
        public BulkCopyContext Context { get; private set; } = null!;
        public List<object> Rows { get; } = new();
        public int SyncCalls { get; private set; }
        public int AsyncCalls { get; private set; }
        public CancellationToken Token { get; private set; }

        public long Write(BulkCopyContext context, IEnumerable<object> rows)
        {
            Context = context;
            Rows.AddRange(rows);
            SyncCalls++;
            return Rows.Count;
        }

        public Task<long> WriteAsync(BulkCopyContext context, IEnumerable<object> rows, CancellationToken cancellationToken)
        {
            Context = context;
            Rows.AddRange(rows);
            AsyncCalls++;
            Token = cancellationToken;
            return Task.FromResult((long)Rows.Count);
        }
    }

    private sealed class InsertingAdapter : IBulkCopyAdapter
    {
        public bool Fail { get; set; }

        public long Write(BulkCopyContext context, IEnumerable<object> rows)
        {
            using var command = context.Connection.CreateCommand();
            command.Transaction = context.Transaction;
            command.CommandText = "INSERT INTO items(Id, Text) VALUES(1, 'adapter')";
            command.ExecuteNonQuery();
            if (Fail) throw new InvalidOperationException("adapter failure");
            return rows.Count();
        }

        public Task<long> WriteAsync(BulkCopyContext context, IEnumerable<object> rows, CancellationToken cancellationToken)
        {
            return Task.FromResult(Write(context, rows));
        }
    }

    private sealed class CountingConnection : DbConnection
    {
        private ConnectionState state;
        public List<string> Statements { get; } = new();
        public override string ConnectionString { get; set; } = "";
        public override string Database => "count";
        public override string DataSource => "count";
        public override string ServerVersion => "1";
        public override ConnectionState State => state;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open() => state = ConnectionState.Open;
        public override void Close() => state = ConnectionState.Closed;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new CountingTransaction(this);
        protected override DbCommand CreateDbCommand() => new CountingCommand(this);
    }

    private sealed class CountingTransaction(CountingConnection owner) : DbTransaction
    {
        protected override DbConnection DbConnection => owner;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public override void Commit() { }
        public override void Rollback() { }
    }

    private sealed class CountingCommand(CountingConnection owner) : DbCommand
    {
        private readonly SqliteCommand parameters = new();
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = owner;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery()
        {
            owner.Statements.Add(CommandText);
            return 1;
        }
        public override object? ExecuteScalar() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        protected override DbParameter CreateDbParameter() => parameters.CreateParameter();
        protected override void Dispose(bool disposing)
        {
            if (disposing) parameters.Dispose();
            base.Dispose(disposing);
        }
    }
}
