using System.Data;
using Daevsoft;
using Daevsoft.Lib;
using Microsoft.Data.Sqlite;

namespace TestDemo.QueryBuilder;

[TestClass]
public class SessionRegressionTests
{
    private static SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE items(id INTEGER PRIMARY KEY, name TEXT, category INTEGER);"
            + "INSERT INTO items VALUES (1,'one',10),(2,'two',10),(3,'three',20);";
        command.ExecuteNonQuery();
        return connection;
    }

    [TestMethod]
    public void ConstructionAndPreviewDoNotOpenConnection()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var db = new NetQL(connection);
        var query = db.Select("items").Where("id", 2).Asc("id").Limit(1);
        Assert.AreEqual(ConnectionState.Closed, connection.State);
        Assert.AreEqual(query.GenerateQuery(), query.GenerateQuery());
        Assert.AreEqual(ConnectionState.Closed, connection.State);
    }

    [TestMethod]
    public void ReadsExecuteFiltersOrderingAndPagingAfterRepeatedPreview()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        var query = db.Select("id", "items").WhereIn("id", new[] { 1, 2, 3 }).Desc("id").Limit(1, 1);
        var sql = query.GenerateQuery();
        Assert.AreEqual(sql, query.GenerateQuery());
        CollectionAssert.AreEqual(new[] { 2 }, query.ReadAsArray<int>().ToArray());
    }

    [TestMethod]
    public void NormalStringValuesNeverBecomeRawSql()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        Assert.AreEqual(0, db.Select("items").Where("name", "!!name").ReadAsList<Item>().Count);
    }

    [TestMethod]
    public void QueryReplacementClearsEveryClauseAndParameter()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        db.Select("category", "items").Where("id", 1).GroupBy("category").Desc("category").Limit(1).AddParameter("unused", 3);
        Assert.AreEqual(3, db.Query("SELECT COUNT(*) FROM items").ReadSingle<int>());
        Assert.AreEqual(3, db.Select("id", "items").ReadAsArray<int>().Count());
    }

    [TestMethod]
    public void GroupByIsClearedAfterExecution()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        Assert.AreEqual(2, db.Select("category", "items").GroupBy("category").ReadAsArray<int>().Count());
        Assert.AreEqual(3, db.Select("id", "items").ReadAsArray<int>().Count());
    }

    [TestMethod]
    public void NestedAndJoinSubqueriesKeepTheirOwnParameters()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        var query = db.Select("i.id", "items i")
            .Join(q => q.Select("id", "items").WhereIn("id", new[] { 2, 3 }), "j", "i.id", "j.id")
            .Wrap(q => q.Where("i.category", 10).OrWhere("i.name", "missing"))
            .WhereIn("i.id", q => q.Select("id", "items").Where("id", ">", 1));
        Assert.AreEqual(query.GenerateQuery(), query.GenerateQuery());
        CollectionAssert.AreEqual(new[] { 2 }, query.ReadAsArray<int>().ToArray());
    }

    [TestMethod]
    public void RawSubqueryParametersPropagate()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        var query = db.Select("id", "items").WhereIn("id", q => q.Query("SELECT id FROM items WHERE id = @wanted")
            .AddParameter("wanted", 2, DbType.Int32));
        CollectionAssert.AreEqual(new[] { 2 }, query.ReadAsArray<int>().ToArray());
    }

    [TestMethod]
    public void EmptyOrMembershipPreservesBooleanMeaning()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        Assert.AreEqual(1, db.Select("items").Where("id", 2).OrWhereIn("id", Array.Empty<int>()).ReadAsList<Item>().Count);
        Assert.AreEqual(3, db.Select("items").Where("id", 2).OrWhereNotIn("id", Array.Empty<int>()).ReadAsList<Item>().Count);
    }

    [TestMethod]
    public void FailedCommandClosesConnectionAndCanBeReused()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var db = new NetQL(connection);
        Assert.ThrowsExactly<SqliteException>(() => db.Query("invalid sql").Execute());
        Assert.AreEqual(ConnectionState.Closed, connection.State);
        Assert.AreEqual(42, db.Query("SELECT 42").ReadSingle<int>());
        Assert.AreEqual(ConnectionState.Closed, connection.State);
    }

    [TestMethod]
    public void CallbackFailureRollsBackOwnedTransactionAndAllowsNewTransaction()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        db.Transaction();
        db.Insert("items").AddValue("id", 4).AddValue("name", "four").Execute();
        var error = new InvalidOperationException("callback failed");
        Assert.AreSame(error, Assert.ThrowsExactly<InvalidOperationException>(() => db.Select("items").Read(_ => throw error)));
        Assert.AreEqual(3, db.Query("SELECT COUNT(*) FROM items").ReadSingle<int>());
        db.Transaction();
        db.Query("INSERT INTO items VALUES(4,'four',20)").Execute();
        db.Commit();
        Assert.AreEqual(4, db.Query("SELECT COUNT(*) FROM items").ReadSingle<int>());
    }

    [TestMethod]
    public void RollbackIsIdempotentAndSessionCanBeReused()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        db.Transaction();
        db.Query("DELETE FROM items").Execute();
        db.Rollback();
        db.Rollback();
        Assert.AreEqual(3, db.Query("SELECT COUNT(*) FROM items").ReadSingle<int>());
    }

    [TestMethod]
    public void DisposingCloneDoesNotDisposeParentConnection()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        using (var clone = db.Clone()) clone.Where("id", 1);
        Assert.AreEqual(3, db.Query("SELECT COUNT(*) FROM items").ReadSingle<int>());
    }

    [TestMethod]
    public void ExistenceChecksUseFiltersWithoutDerivedTableRestrictions()
    {
        using var db = new NetQL(OpenDatabase());
        db.IsKeepAlive(true);
        Assert.IsFalse(db.Select("items").Where("id", 99).Asc("id").IsExist());
        Assert.IsTrue(db.Select("items").Where("id", 2).Asc("id").IsExist());
    }

    [TestMethod]
    [DataRow(Provider.SqlServer, "[items]", "OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY")]
    [DataRow(Provider.Oracle, "\"items\"", "OFFSET 2 ROWS FETCH NEXT 3 ROWS ONLY")]
    [DataRow(Provider.MySql, "`items`", "LIMIT 3 OFFSET 2")]
    [DataRow(Provider.PostgreSQL, "\"items\"", "LIMIT 3 OFFSET 2")]
    [DataRow(Provider.SQLite, "\"items\"", "LIMIT 3 OFFSET 2")]
    public void ProviderPagingAndQuotingAreGeneratedOffline(Provider provider, string table, string paging)
    {
        using var connection = new SqliteConnection();
        using var db = new NetQL(connection, provider);
        var sql = db.Select("items").Asc("id").Limit(2, 3).GenerateQuery();
        StringAssert.Contains(sql, "FROM " + table);
        StringAssert.Contains(sql, paging);
        Assert.AreEqual(ConnectionState.Closed, connection.State);
    }

    [TestMethod]
    public void InvalidPagingIsRejectedRatherThanReturningUnboundedData()
    {
        using var db = new NetQL(new SqliteConnection());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => db.Limit(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => db.Limit(-1, 5));
    }

    [TestMethod]
    public void SqlServerPagingRequiresExplicitStableOrdering()
    {
        using var db = new NetQL(new SqliteConnection(), Provider.SqlServer);
        Assert.ThrowsExactly<InvalidOperationException>(() => db.Select("items").Limit(2).GenerateQuery());
    }

    public sealed class Item
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Category { get; set; }
    }
}
