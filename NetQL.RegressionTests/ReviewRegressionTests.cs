using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;
using Daevsoft.Lib;
using Microsoft.Data.Sqlite;

namespace Daevsoft.RegressionTests;

[TestClass]
public class ReviewRegressionTests
{
    private static DbUtils OpenDatabase()
    {
        var db = new DbUtils(new SqliteConnection("Data Source=:memory:"), Provider.SQLite);
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(id INTEGER PRIMARY KEY, text TEXT, score INTEGER);" +
            "INSERT INTO items VALUES (1,'alpha',10),(2,NULL,20)").ExecuteNonQuery();
        return db;
    }

    [TestMethod]
    public void SingleObjectInsertRespectsNotMappedLikeBulkInsert()
    {
        using var db = OpenDatabase();
        Assert.AreEqual(1, db.Insert("items", new InsertRow { Id = 3 }).ExecuteNonQuery());
        Assert.AreEqual(3, db.Query("SELECT COUNT(*) FROM items").ReadSingle<int>());
    }

    [TestMethod]
    public void WriteSubqueryRendersRetainedChildFiltersTogetherWithParameters()
    {
        using var db = OpenDatabase();
        DbUtils child = null!;
        var write = db.Delete("items").WhereIn("id", q =>
        {
            child = q;
            return q.Select("id", "items").Where("id", 1);
        });
        child.Where("score", 999);
        Assert.AreEqual(0, write.ExecuteNonQuery(), "The additional child filter matches no rows.");
        Assert.AreEqual(2, db.Query("SELECT COUNT(*) FROM items").ReadSingle<int>());
    }

    [TestMethod]
    public void JoinRendersRetainedChildFiltersTogetherWithParameters()
    {
        using var db = OpenDatabase();
        DbUtils child = null!;
        var query = db.Select("i.id", "items i").Join(q =>
        {
            child = q;
            return q.Select("id", "items").Where("id", 1);
        }, "j", "i.id", "j.id");
        child.Where("score", 999);
        Assert.AreEqual(0, query.ReadAsArray<int>().Count(), "The additional join filter matches no rows.");
    }

    [TestMethod]
    public void WriteNullParameterDoesNotDiscardCustomBindingExpression()
    {
        using var db = OpenDatabase();
        db.Update("items").SetValue("score", 99)
            .Where<string?>("text", null, bind => "COALESCE(" + bind + ", 'alpha')")
            .ExecuteNonQuery();
        Assert.AreEqual(99, db.Query("SELECT score FROM items WHERE id = 1").ReadSingle<int>());
        Assert.AreEqual(20, db.Query("SELECT score FROM items WHERE id = 2").ReadSingle<int>());
    }

    [TestMethod]
    public void ParentReturnedByWrapIsRejectedBeforeCreatingRecursiveQueryGraph()
    {
        using var db = OpenDatabase();
        db.Select("items");
        // Do not call GenerateQuery after acceptance: the resulting graph stack-overflows.
        Assert.Throws<ArgumentException>(() => db.Wrap(_ => db));
    }

    [TestMethod]
    public void ReaderCleanupFailureDoesNotReplaceCallbackFailure()
    {
        using var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Rows.Add(1);
        using var source = table.CreateDataReader();
        var cleanup = new ApplicationException("reader disposal failed");
        var reader = Proxy<IDataReader>((method, arguments) =>
        {
            if (method.Name == "Dispose") throw cleanup;
            return method.Invoke(source, arguments);
        });
        using var db = new DbUtils(FakeConnection(reader: reader), Provider.SQLite);
        var callbackError = new InvalidOperationException("callback failed");
        var observed = Capture(() => db.Query("SELECT 1").Read(_ => throw callbackError));
        Assert.AreSame(callbackError, observed, "Observed: " + observed);
    }

    [TestMethod]
    public void TransactionCleanupFailureDoesNotReplaceCommitFailure()
    {
        var commitError = new InvalidOperationException("commit outcome unknown");
        var cleanupError = new ApplicationException("transaction disposal failed");
        using var db = new DbUtils(FakeConnection(commit: () => throw commitError,
            disposeTransaction: () => throw cleanupError), Provider.SQLite);
        db.Transaction().Query("UPDATE items SET score = 1").ExecuteNonQuery();
        var observed = Capture(() => db.Commit());
        Assert.AreSame(commitError, observed, "Observed: " + observed);
    }

    [TestMethod]
    public async Task SyncCommitHoldsExecutionGateUntilTransactionCompletion()
    {
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var db = new DbUtils(FakeConnection(commit: () =>
        {
            entered.Set();
            if (!resume.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test release timed out");
        }), Provider.SQLite);
        db.Transaction().Query("UPDATE items SET score = 1").ExecuteNonQuery();
        var committing = Task.Run(() => db.Commit());
        Exception? overlapError;
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
            overlapError = Capture(() => db.Query("UPDATE items SET score = 2").ExecuteNonQuery());
        }
        finally
        {
            resume.Set();
            await committing;
        }
        Assert.IsInstanceOfType<InvalidOperationException>(overlapError,
            "A second command must not execute while Commit is inside the provider.");
    }

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    private static IDbConnection FakeConnection(IDataReader? reader = null,
        Action? commit = null, Action? disposeTransaction = null)
    {
        var state = ConnectionState.Closed;
        IDbConnection connection = null!;
        connection = Proxy<IDbConnection>((method, arguments) =>
        {
            switch (method.Name)
            {
                case "get_State": return state;
                case "Open": state = ConnectionState.Open; return null;
                case "Close": state = ConnectionState.Closed; return null;
                case "CreateCommand":
                    return Proxy<IDbCommand>((commandMethod, commandArguments) =>
                    {
                        switch (commandMethod.Name)
                        {
                            case "ExecuteNonQuery": return 1;
                            case "ExecuteReader": return reader;
                            case "CreateParameter": return new SqliteParameter();
                            default: return null;
                        }
                    });
                case "BeginTransaction":
                    return Proxy<IDbTransaction>((transactionMethod, transactionArguments) =>
                    {
                        switch (transactionMethod.Name)
                        {
                            case "get_Connection": return connection;
                            case "Commit": commit?.Invoke(); return null;
                            case "Dispose": disposeTransaction?.Invoke(); return null;
                            default: return null;
                        }
                    });
                default: return null;
            }
        });
        return connection;
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    public sealed class InsertRow
    {
        [Column("id")] public int Id { get; set; }
        [NotMapped] public string DisplayLabel => "not a database column";
    }
}
