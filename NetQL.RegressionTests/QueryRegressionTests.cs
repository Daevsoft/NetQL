using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
using System.Data;
using Daevsoft.Lib;
using Microsoft.Data.Sqlite;

namespace Daevsoft.RegressionTests;

[TestClass]
public class QueryRegressionTests
{
    [TestMethod]
    public void QualifiedNamesAndAliasesAreQuotedForEveryProvider()
    {
        foreach (var provider in Enum.GetValues<Provider>())
        {
            using var db = new NetQL(new SqliteConnection(), provider);
            var sql = db.Select("x.id AS identifier", "schema.items AS x").GenerateQuery();
            string left = provider == Provider.SqlServer ? "[" : provider == Provider.MySql ? "`" : "\"";
            string right = provider == Provider.SqlServer ? "]" : left;
            string tableAlias = provider == Provider.Oracle ? " " : " AS ";
            Assert.AreEqual($"SELECT {left}x{right}.{left}id{right} AS {left}identifier{right} FROM {left}schema{right}.{left}items{right}{tableAlias}{left}x{right}", sql);
        }
    }

    [TestMethod]
    public void DelimitersInsideIdentifiersAreEscaped()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE \"odd\"\"table\" (\"odd\"\"column\" INTEGER)").Execute();
        db.Insert("odd\"table").AddValue("odd\"column", 7).Execute();
        Assert.AreEqual(7, db.Select("odd\"column", "odd\"table").ReadSingle<int>());
    }

    [TestMethod]
    public void QualifiedAliasQueryExecutes()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(id INTEGER); INSERT INTO items VALUES(8)").Execute();
        Assert.AreEqual(8, db.Select("s.id AS result", "main.items AS s").ReadSingle<int>());
    }

    [TestMethod]
    public void PocoMappingHandlesNativeValuesNullableAndIgnoredProperties()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        var id = Guid.NewGuid();
        var row = db.Query("SELECT @id AS identifier, @bytes AS payload, 2 AS state, NULL AS missing, 3 AS ReadOnly, 9 AS Ignore")
            .AddParameter("id", id, DbType.Guid)
            .AddParameter("bytes", new byte[] { 0, 128, 255 }, DbType.Binary).ReadAs<MappedItem>();
        Assert.AreEqual(id, row.Id);
        CollectionAssert.AreEqual(new byte[] { 0, 128, 255 }, row.Payload);
        Assert.AreEqual(State.Ready, row.State);
        Assert.IsNull(row.Missing);
        Assert.AreEqual(0, row.Ignore);
    }

    [TestMethod]
    public void CommonClrTypesMapToNativeDbTypes()
    {
        var mapper = new TypeProbe();
        Assert.AreEqual(DbType.Int16, mapper.Map(typeof(short?)));
        Assert.AreEqual(DbType.Byte, mapper.Map(typeof(byte)));
        Assert.AreEqual(DbType.Single, mapper.Map(typeof(float)));
        Assert.AreEqual(DbType.Guid, mapper.Map(typeof(Guid?)));
        Assert.AreEqual(DbType.Binary, mapper.Map(typeof(byte[])));
        Assert.AreEqual(DbType.DateTimeOffset, mapper.Map(typeof(DateTimeOffset)));
        Assert.AreEqual(DbType.Time, mapper.Map(typeof(TimeSpan)));
        Assert.AreEqual(DbType.Int32, mapper.Map(typeof(State)));
    }

    [TestMethod]
    public void NullOperatorsHaveConsistentSqlSemantics()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(id INTEGER, value TEXT); INSERT INTO items VALUES(1,NULL),(2,'present')").Execute();
        Assert.AreEqual(1, db.Select("id", "items").Where("value", "=", (object)null!).ReadSingle<int>());
        Assert.AreEqual(2, db.Select("id", "items").Where("value", "!=", (object)null!).ReadSingle<int>());
    }

    [TestMethod]
    public void MultipleOrderColumnsEachUseRequestedDirection()
    {
        using var db = new NetQL(new SqliteConnection());
        StringAssert.Contains(db.Select("items").Desc("category,id").GenerateQuery(), "ORDER BY \"category\" DESC,\"id\" DESC");
    }

    [TestMethod]
    public void OracleOmitsAsForTableAliasesOnly()
    {
        using var db = new NetQL(new SqliteConnection(), Provider.Oracle);
        var sql = db.Select("i.id AS identifier", "items AS i").Join("other AS o", "i.id", "o.id").GenerateQuery();
        Assert.AreEqual("SELECT \"i\".\"id\" AS \"identifier\" FROM \"items\" \"i\" INNER JOIN \"other\" \"o\" ON \"i\".\"id\"=\"o\".\"id\"", sql);
    }

    [TestMethod]
    public void StringConstructorSupportsInstalledProviderWithoutEagerConnection()
    {
        using var db = new DbUtils("Data Source=:memory:", Provider.SQLite);
        Assert.AreEqual(8, db.Query("SELECT 8").ReadSingle<int>());
    }

    [TestMethod]
    public void OracleChunksInListsAboveOneThousandExpressions()
    {
        using var db = new NetQL(new SqliteConnection(), Provider.Oracle);
        var ids = Enumerable.Range(1, 2500).ToArray();
        string sql = db.Select("id", "items").WhereIn("id", ids).GenerateQuery();
        Assert.AreEqual(3, Regex.Matches(sql, "\\\"id\\\" IN \\(").Count);
        StringAssert.Contains(sql, ") OR \"id\" IN (");

        string notIn = db.Select("id", "items").WhereNotIn("id", ids).GenerateQuery();
        Assert.AreEqual(3, Regex.Matches(notIn, "\\\"id\\\" NOT IN \\(").Count);
        StringAssert.Contains(notIn, ") AND \"id\" NOT IN (");
    }

    [TestMethod]
    public void OracleChunkedListsKeepMembershipSemantics()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"), Provider.Oracle);
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(id INTEGER); " +
            "WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 2500) " +
            "INSERT INTO items SELECT n FROM seq").Execute();
        var ids = Enumerable.Range(1, 2500).ToArray();
        Assert.AreEqual(2500L, db.Select("COUNT(*)", "items").WhereIn("id", ids).ReadSingle<long>());
        Assert.AreEqual(0L, db.Select("COUNT(*)", "items").WhereNotIn("id", ids).ReadSingle<long>());
        Assert.AreEqual(1500L, db.Select("COUNT(*)", "items").WhereIn("id", ids.Take(1500).ToArray()).ReadSingle<long>());
        Assert.AreEqual(2500, db.Delete("items").WhereIn("id", ids).ExecuteNonQuery());
    }

    [TestMethod]
    public void OracleChunksWriteInListsAboveOneThousandExpressions()
    {
        using var db = new NetQL(new SqliteConnection(), Provider.Oracle);
        string sql = db.Delete("items").WhereIn("id", Enumerable.Range(1, 1500).ToArray()).GenerateQuery();
        Assert.AreEqual(2, Regex.Matches(sql, "\\\"id\\\" IN \\(").Count);
    }

    [TestMethod]
    public void SqlServerBulkRejectsRowAndParameterOverflowsBeforeTouchingProvider()
    {
        using var db = new NetQL(new SqliteConnection(), Provider.SqlServer);
        var rows = Enumerable.Range(0, 1001).Select(i => new BulkRow { Id = i, Text = "row" }).ToList();
        var rowsError = Assert.ThrowsExactly<InvalidOperationException>(() => db.Insert("items", rows).ExecuteNonQuery());
        StringAssert.Contains(rowsError.Message, "1000 rows");

        var wide = Enumerable.Range(0, 700).Select(i => new { Id = i, Text = "row", Score = i, Note = "n" }).ToList();
        var parameterError = Assert.ThrowsExactly<InvalidOperationException>(() => db.Insert("items", wide).ExecuteNonQuery());
        StringAssert.Contains(parameterError.Message, "2100 parameters");
    }

    [TestMethod]
    public void PocoMappingHonorsAliasesInitSettersAndDefaults()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        var id = Guid.NewGuid();
        var row = db.Query("SELECT @id AS identifier, 'ready' AS state, NULL AS missing, 4 AS private_value, 5 AS ReadOnly, 6 AS Ignore, 7 AS _hidden, 8 AS untyped")
            .AddParameter("id", id, DbType.Guid)
            .ReadAs<AliasedItem>();
        Assert.AreEqual(id, row.Id);
        Assert.AreEqual(State.Ready, row.State);
        Assert.IsNull(row.Missing);
        Assert.AreEqual(0, row.PrivateValue);
        Assert.AreEqual(0, row.ReadOnly);
        Assert.AreEqual(0, row.Ignore);
        Assert.AreEqual(7, row.Hidden);
        Assert.AreEqual(8, row.Untyped);
    }

    [TestMethod]
    public void StructRowsAreMappedThroughBoxedWrites()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        var row = db.Query("SELECT 11 AS Id, 'open' AS Name").ReadAs<StructRow>();
        Assert.AreEqual(11, row.Id);
        Assert.AreEqual("open", row.Name);
    }

    [TestMethod]
    public void ChunkedBulkWritesEveryChunkAndReportsTheTotal()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(Id INTEGER PRIMARY KEY, Text TEXT)").Execute();
        var rows = Enumerable.Range(1, 25).Select(i => new BulkRow { Id = i, Text = "row" + i }).ToList();
        Assert.AreEqual(25, db.Insert("items").Bulk(rows, 10).ExecuteNonQuery());
        Assert.AreEqual(25L, db.Select("COUNT(*)", "items").ReadSingle<long>());
        Assert.AreEqual("row25", db.Select("Text", "items").Where("Id", 25).ReadSingle<string>());

        int reported = 0;
        db.Delete("items").ExecuteNonQuery();
        db.Insert("items").Bulk(rows, 10).Execute(value => reported = (int)value);
        Assert.AreEqual(25, reported);
    }

    [TestMethod]
    public void ChunkedBulkRollsBackEveryChunkByDefault()
    {
        using var db = OpenBulkTable(out var rows);
        Assert.ThrowsExactly<SqliteException>(() => db.Insert("items").Bulk(rows, 10).ExecuteNonQuery());
        Assert.AreEqual(0L, db.Select("COUNT(*)", "items").ReadSingle<long>());
    }

    [TestMethod]
    public void ChunkedBulkCanCommitEachChunkExplicitly()
    {
        using var db = OpenBulkTable(out var rows);
        Assert.ThrowsExactly<SqliteException>(() => db.Insert("items").Bulk(rows, 10, commitEachChunk: true).ExecuteNonQuery());
        Assert.AreEqual(10L, db.Select("COUNT(*)", "items").ReadSingle<long>());
    }

    [TestMethod]
    public async Task ChunkedBulkAsyncWritesAllRows()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(Id INTEGER PRIMARY KEY, Text TEXT)").Execute();
        var rows = Enumerable.Range(1, 30).Select(i => new BulkRow { Id = i, Text = "row" + i }).ToList();
        Assert.AreEqual(30, await db.Insert("items").Bulk(rows, 7).ExecuteAsync());
        Assert.AreEqual(30L, db.Select("COUNT(*)", "items").ReadSingle<long>());
    }

    [TestMethod]
    public void ChunkedBulkLeavesACallerTransactionOpenUntilItIsCompleted()
    {
        using var db = new NetQL(new SqliteConnection("Data Source=:memory:"));
        db.IsKeepAlive(true);
        db.Query("CREATE TABLE items(Id INTEGER PRIMARY KEY, Text TEXT)").Execute();
        var rows = Enumerable.Range(1, 20).Select(i => new BulkRow { Id = i, Text = "row" }).ToList();
        db.Transaction();
        Assert.AreEqual(20, db.Insert("items").Bulk(rows, 6).ExecuteNonQuery());
        Assert.AreEqual(20L, db.Select("COUNT(*)", "items").ReadSingle<long>());
        db.Rollback();
        Assert.AreEqual(0L, db.Select("COUNT(*)", "items").ReadSingle<long>());
    }

    [TestMethod]
    public void ChunkedBulkRejectsCommitEachChunkInsideAConfiguredTransaction()
    {
        using var db = OpenBulkTable(out var rows);
        db.Transaction();
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            db.Insert("items").Bulk(rows, 10, commitEachChunk: true).ExecuteNonQuery());
        StringAssert.Contains(error.Message, "commitEachChunk");
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

    public sealed class AliasedItem
    {
        [Column("identifier")]
        public Guid Id { get; init; }
        public State State { get; init; }
        public decimal? Missing { get; init; }
        public int PrivateValue { get; private set; }
        public int ReadOnly => 0;
        [NotMapped]
        public int Ignore { get; set; }
        [Column("_hidden")]
        public int Hidden { get; set; }
        public int Untyped { get; set; }
    }

    public struct StructRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    private sealed class TypeProbe : QueryCommon
    {
        public DbType Map(Type type) => GetType(type);
    }

    public enum State { Unknown, Pending, Ready }
    public sealed class MappedItem
    {
        [Column("identifier")]
        public Guid Id { get; set; }
        public byte[] Payload { get; set; } = [];
        public State State { get; set; }
        public decimal? Missing { get; set; }
        public int ReadOnly => 0;
        [NotMapped]
        public int Ignore { get; set; }
        public string this[int index] { get => ""; set { } }
    }
}
