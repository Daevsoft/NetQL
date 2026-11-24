using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using Daevsoft.Lib;
using Microsoft.Data.Sqlite;

namespace TestDemo.QueryBuilder;

[TestClass]
public class WriteRegressionTests
{
    private SqliteConnection connection = null!;
    private DbUtils db = null!;

    [TestInitialize]
    public void Initialize()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY, text TEXT, score INTEGER);" +
            "INSERT INTO items VALUES (1, 'alpha', 10), (2, NULL, 20), (3, 'beta', 30);";
        command.ExecuteNonQuery();
        db = new DbUtils(connection, Provider.SQLite);
        db.IsKeepAlive(true);
    }

    [TestCleanup]
    public void Cleanup()
    {
        db.Dispose();
        connection.Dispose();
    }

    private object Scalar(string sql, object? value = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@value", value ?? DBNull.Value);
        return command.ExecuteScalar()!;
    }

    private static int Execute(SqlModel model)
    {
        var affected = -1;
        model.Execute(result => affected = (int)result);
        return affected;
    }

    [TestMethod]
    [DataRow("!!upper('owned')")]
    [DataRow("!!'x'); DELETE FROM items; --")]
    [DataRow("!!")]
    [DataRow("O'Brien; -- literal")]
    public void Insert_BindsLiteralText(string text)
    {
        Assert.AreEqual(1, Execute(db.Insert("items").AddValue("id", 4).AddValue("text", text)));
        Assert.AreEqual(text, Scalar("SELECT text FROM items WHERE id = 4"));
        Assert.AreEqual(4L, Scalar("SELECT count(*) FROM items"));
    }

    [TestMethod]
    [DataRow("!!upper('owned')")]
    [DataRow("!!'x', score = 999 --")]
    [DataRow("O'Brien")]
    public void Update_BindsLiteralText(string text)
    {
        Assert.AreEqual(1, Execute(db.Update("items").SetValue("text", text).Where("id", 1)));
        Assert.AreEqual(text, Scalar("SELECT text FROM items WHERE id = 1"));
        Assert.AreEqual(10L, Scalar("SELECT score FROM items WHERE id = 1"));
        Assert.AreEqual("beta", Scalar("SELECT text FROM items WHERE id = 3"));
    }

    [TestMethod]
    [DataRow("!!'alpha' OR 1 = 1 --")]
    [DataRow("!!upper('alpha')")]
    [DataRow("O'Brien")]
    public void Where_BindsLiteralText(string text)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE items SET text = @text WHERE id = 1";
            command.Parameters.AddWithValue("@text", text);
            command.ExecuteNonQuery();
        }
        Assert.AreEqual(1, Execute(db.Update("items").SetValue("score", 99).Where("text", text)));
        Assert.AreEqual(1L, Scalar("SELECT count(*) FROM items WHERE score = 99"));
    }

    [TestMethod]
    [DataRow(false, false, 1)]
    [DataRow(false, true, 1)]
    [DataRow(true, false, 3)]
    [DataRow(true, true, 3)]
    public void EmptyOrLists_PreserveBooleanOperator(bool notIn, bool nullList, int expected)
    {
        var model = db.Update("items").SetValue("score", 99).Where("id", 1);
        var list = nullList ? null! : Array.Empty<int>();
        if (notIn) model.OrWhereNotIn("id", list);
        else model.OrWhereIn("id", list);
        Assert.AreEqual(expected, Execute(model));
        Assert.AreEqual((long)expected, Scalar("SELECT count(*) FROM items WHERE score = 99"));
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 1)]
    public void EmptyAndLists_RenderFalseOrTrue(bool notIn, int expected)
    {
        var model = db.Delete("items").Where("id", 1);
        if (notIn) model.WhereNotIn("id", Array.Empty<int>());
        else model.WhereIn("id", Array.Empty<int>());
        Assert.AreEqual(expected, Execute(model));
        Assert.AreEqual(3L - expected, Scalar("SELECT count(*) FROM items"));
    }

    [TestMethod]
    [DataRow("generic", 1)]
    [DataRow("object", 1)]
    [DataRow("custom", 0)]
    [DataRow("equal", 1)]
    [DataRow("notEqual", 2)]
    [DataRow("notEqualSql", 2)]
    [DataRow("orGeneric", 1)]
    [DataRow("orEqual", 1)]
    [DataRow("orNotEqual", 2)]
    [DataRow("dbNull", 1)]
    public void NullPredicates_UseSqlNullSemantics(string variant, int expected)
    {
        var model = db.Update("items").SetValue("score", 99);
        switch (variant)
        {
            case "generic": model.Where<string?>("text", null); break;
            case "object": model.Where("text", (object)null!); break;
            case "custom": model.Where<string?>("text", null, bind => bind); break;
            // A typed null selects the operator overload, not the (value, customBind) overload.
            case "equal": model.Where<string?>("text", "=", (string?)null); break;
            case "notEqual": model.Where<string?>("text", "!=", (string?)null); break;
            case "notEqualSql": model.Where<string?>("text", "<>", (string?)null); break;
            case "orGeneric": model.Where("id", -1).OrWhere<string?>("text", null); break;
            case "orEqual": model.Where("id", -1).OrWhere<string?>("text", "=", (string?)null); break;
            case "orNotEqual": model.Where("id", -1).OrWhere<string?>("text", "<>", (string?)null); break;
            case "dbNull": model.Where("text", DBNull.Value); break;
        }
        Assert.AreEqual(expected, Execute(model));
        Assert.AreEqual((long)expected, Scalar("SELECT count(*) FROM items WHERE score = 99"));
    }

    [TestMethod]
    public void ExplicitRawExpressions_AndLocalParameters_AreApplied()
    {
        var model = db.Update("items")
            .SetRawValue("score", "score + @increment")
            .SetRawValue("text", "upper(text)")
            .WhereRaw("id = @target")
            .AddParameter("increment", 5, DbType.Int32)
            .AddParameter("target", 1, DbType.Int32);
        Assert.AreEqual(1, Execute(model));
        Assert.AreEqual(15L, Scalar("SELECT score FROM items WHERE id = 1"));
        Assert.AreEqual("ALPHA", Scalar("SELECT text FROM items WHERE id = 1"));
    }

    [TestMethod]
    public void Bulk_BindsLiteralTextNullAndMappedColumns()
    {
        var model = db.Insert("items").Bulk(new[]
        {
            new MappedRow { Id = 4, Text = "!!upper('owned')", Score = null },
            new MappedRow { Id = 5, Text = null, Score = 50 }
        });
        Assert.AreEqual(2, Execute(model));
        Assert.AreEqual("!!upper('owned')", Scalar("SELECT text FROM items WHERE id = 4"));
        Assert.AreEqual(DBNull.Value, Scalar("SELECT score FROM items WHERE id = 4"));
        Assert.AreEqual(DBNull.Value, Scalar("SELECT text FROM items WHERE id = 5"));
        Assert.AreEqual(50L, Scalar("SELECT score FROM items WHERE id = 5"));
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("null")]
    [DataRow("nullRow")]
    [DataRow("string")]
    [DataRow("scalar")]
    [DataRow("nestedList")]
    [DataRow("noColumns")]
    [DataRow("noFirstColumns")]
    [DataRow("mismatch")]
    [DataRow("indexer")]
    [DataRow("duplicate")]
    public void Bulk_RejectsMalformedRowsBeforeWriting(string variant)
    {
        object? rows = variant switch
        {
            "empty" => Array.Empty<MappedRow>(),
            "null" => null,
            "nullRow" => new object?[] { new { id = 4 }, null },
            "string" => "not a row",
            "scalar" => new[] { 4, 5 },
            "nestedList" => new object[] { new[] { new { id = 4 } } },
            "noColumns" => new[] { new { _ignored = 1 } },
            "noFirstColumns" => new object[] { new { _ignored = 1 }, new { id = 4 } },
            "mismatch" => new object[] { new { id = 4, text = "four" }, new { id = 5, score = 50 } },
            "indexer" => new[] { new IndexerRow() },
            "duplicate" => new[] { new DuplicateRow() },
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<ArgumentException>(() => { db.Insert("items").Bulk(rows!).Execute(); });
        Assert.AreEqual(3L, Scalar("SELECT count(*) FROM items"));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Bulk_RejectsMixedExplicitValues(bool bulkFirst)
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            var model = db.Insert("items");
            if (bulkFirst) model.Bulk(new[] { new { id = 4 } }).AddValue("text", "four");
            else model.AddValue("text", "four").Bulk(new[] { new { id = 4 } });
            model.Execute();
        });
        Assert.AreEqual(3L, Scalar("SELECT count(*) FROM items"));
    }

    [TestMethod]
    public void UpdateBulk_RejectsEnumerableRatherThanReflectingListProperties()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            db.Update("items").Bulk(new List<MappedRow> { new MappedRow { Id = 1 } }).Execute();
        });
        Assert.AreEqual(60L, Scalar("SELECT sum(score) FROM items"));
    }

    [TestMethod]
    public void DeleteBuilder_RejectsSecondExecutionWithoutDeletingRemainingRows()
    {
        var model = db.Delete("items").Where("id", 1);
        Assert.AreEqual(1, Execute(model));
        Assert.Throws<InvalidOperationException>(() => { model.Execute(); });
        Assert.AreEqual(2L, Scalar("SELECT count(*) FROM items"));
    }

    [TestMethod]
    public void NestedWhere_PropagatesExplicitParameters()
    {
        var model = db.Update("items").SetValue("score", 99)
            .Wrap(child => child.WhereRaw("id = @target").AddParameter("target", 1, DbType.Int32));
        Assert.AreEqual(1, Execute(model));
        Assert.AreEqual(1L, Scalar("SELECT count(*) FROM items WHERE score = 99"));
    }

    [TestMethod]
    [DataRow("insert", 1)]
    [DataRow("update", 2)]
    [DataRow("delete", 0)]
    [DataRow("bulk", 2)]
    public void ExecuteNonQuery_ReturnsAffectedRows(string operation, int expected)
    {
        var model = operation switch
        {
            "insert" => db.Insert("items").AddValue("id", 4),
            "update" => db.Update("items").SetValue("score", 99).Where("id", ">", 1),
            "delete" => db.Delete("items").Where("id", -1),
            "bulk" => db.Insert("items").Bulk(new[] { new { id = 4 }, new { id = 5 } }),
            _ => throw new InvalidOperationException()
        };
        Assert.AreEqual(expected, model.ExecuteNonQuery());
        Assert.Throws<InvalidOperationException>(() => model.ExecuteNonQuery());
    }

    [TestMethod]
    public async Task ExecuteAsync_ReturnsAffectedRows_AndEnforcesSingleUse()
    {
        var model = db.Update("items").SetValue("score", 99).Where("id", 1);
        Assert.AreEqual(1, await model.ExecuteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await model.ExecuteAsync());
        Assert.AreEqual(1L, Scalar("SELECT count(*) FROM items WHERE score = 99"));
    }

    [TestMethod]
    public async Task ExecuteAsync_HonorsPreCancellationWithoutWriting()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var model = db.Delete("items").Where("id", 1);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await model.ExecuteAsync(cancellation.Token));
        Assert.AreEqual(3L, Scalar("SELECT count(*) FROM items"));
    }

    [TestMethod]
    [DataRow("insert")]
    [DataRow("update")]
    [DataRow("delete")]
    [DataRow("bulk")]
    public void GenerateQuery_IsPureAndDoesNotDuplicateParameters(string operation)
    {
        var model = operation switch
        {
            "insert" => db.Insert("items").AddValue("id", 4).AddRawValue("text", "upper(@text)").AddParameter("text", "four"),
            "update" => db.Update("items").SetValue("text", "changed").Where("id", 1).OrWhereIn("id", Array.Empty<int>()),
            "delete" => db.Delete("items").Where("id", 1),
            "bulk" => db.Insert("items").Bulk(new[] { new { id = 4, text = "four" }, new { id = 5, text = "five" } }),
            _ => throw new InvalidOperationException()
        };
        var sql = model.GenerateQuery();
        Assert.AreEqual(sql, model.GenerateQuery());
        Assert.AreEqual(3L, Scalar("SELECT count(*) FROM items"));
        Assert.AreEqual(operation == "bulk" ? 2 : 1, model.ExecuteNonQuery());
        Assert.AreEqual(sql, model.GenerateQuery());
        if (operation == "insert") Assert.AreEqual("FOUR", Scalar("SELECT text FROM items WHERE id = 4"));
    }

    [TestMethod]
    [DataRow(Provider.Oracle, "INSERT ALL", "SELECT 1 FROM DUAL")]
    [DataRow(Provider.SQLite, "INSERT INTO", "),(")]
    [DataRow(Provider.PostgreSQL, "INSERT INTO", "),(")]
    [DataRow(Provider.SqlServer, "INSERT INTO", "),(")]
    [DataRow(Provider.MySql, "INSERT INTO", "),(")]
    public void Bulk_GeneratesProviderDialectWithoutOpeningConnection(Provider provider, string prefix, string fragment)
    {
        using var unopened = new SqliteConnection("Data Source=:memory:");
        using var dialectDb = new DbUtils(unopened, provider);
        var model = dialectDb.Insert("items").Bulk(new[] { new { id = 4 }, new { id = 5 } });
        var sql = model.GenerateQuery();
        StringAssert.StartsWith(sql, prefix);
        StringAssert.Contains(sql, fragment);
        if (provider == Provider.Oracle)
        {
            Assert.AreEqual(2, sql.Split("INTO \"items\"").Length - 1);
            Assert.IsFalse(sql.Contains("),("));
        }
        Assert.AreEqual(sql, model.GenerateQuery());
        Assert.AreEqual(ConnectionState.Closed, unopened.State);
    }

    [TestMethod]
    public void RawIdentifierExpression_DoesNotMakeValueRaw()
    {
        var model = db.Update("items").SetValue("score", 99).Where(Str.Raw("lower(text)"), "alpha");
        Assert.AreEqual(1, model.ExecuteNonQuery());
        Assert.AreEqual(1L, Scalar("SELECT count(*) FROM items WHERE score = 99"));
    }

    [TestMethod]
    public void Subquery_PropagatesJoinedAndNestedRawParameters()
    {
        var model = db.Delete("items").WhereIn("id", child => child.Select("i.id", "items i")
            .Join(join => join.Select("id", "items").WhereRaw("score = @score")
                .AddParameter("score", 10, DbType.Int32), "filtered", "i.id", "filtered.id")
            .Wrap(wrapped => wrapped.WhereRaw("i.id = @id").AddParameter("id", 1, DbType.Int32)));
        Assert.AreEqual(1, model.ExecuteNonQuery());
        Assert.AreEqual(2L, Scalar("SELECT count(*) FROM items"));
    }

    [TestMethod]
    public void FailedExecution_CannotBeRetriedWithDifferentMutation()
    {
        var model = db.Insert("items").AddValue("id", 1);
        Assert.Throws<SqliteException>(() => model.ExecuteNonQuery());
        model.Insert("other_table");
        Assert.Throws<InvalidOperationException>(() => model.ExecuteNonQuery());
        Assert.AreEqual(3L, Scalar("SELECT count(*) FROM items"));
    }

    [TestMethod]
    public void Bulk_ReordersMatchingColumnsAndBindsMultipleBatchesOnce()
    {
        var model = db.Insert("items").Bulk(new object[]
        {
            new { id = 4, text = "four" }, new { text = "five", id = 5 }
        }).Bulk(new { text = "six", id = 6 });
        Assert.AreEqual(model.GenerateQuery(), model.GenerateQuery());
        Assert.AreEqual(3, model.ExecuteNonQuery());
        Assert.AreEqual("four", Scalar("SELECT text FROM items WHERE id = 4"));
        Assert.AreEqual("five", Scalar("SELECT text FROM items WHERE id = 5"));
        Assert.AreEqual("six", Scalar("SELECT text FROM items WHERE id = 6"));
    }

    [TestMethod]
    public void UpdateBulk_MapsColumnsAndKeepsRawLookingTextLiteral()
    {
        var row = new MappedRow { Id = 1, Text = "!!upper('owned')", Score = null };
        Assert.AreEqual(1, db.Update("items").Bulk(row).Where("id", 1).ExecuteNonQuery());
        Assert.AreEqual("!!upper('owned')", Scalar("SELECT text FROM items WHERE id = 1"));
        Assert.AreEqual(DBNull.Value, Scalar("SELECT score FROM items WHERE id = 1"));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("empty")]
    [DataRow("indexer")]
    [DataRow("string")]
    public void UpdateBulk_RejectsInvalidObjectsBeforeWriting(string variant)
    {
        object? row = variant switch
        {
            "null" => null,
            "empty" => new { _ignored = 1 },
            "indexer" => new IndexerRow(),
            "string" => "not a row",
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<ArgumentException>(() => db.Update("items").Bulk(row!).ExecuteNonQuery());
        Assert.AreEqual(60L, Scalar("SELECT sum(score) FROM items"));
    }

    [TestMethod]
    public void CustomBindings_StillBindLiteralValues()
    {
        var model = db.Update("items").SetValue("text", "!!alpha", bind => "upper(" + bind + ")")
            .Where("id", 1, bind => bind + " + 0");
        Assert.AreEqual(1, model.ExecuteNonQuery());
        Assert.AreEqual("!!ALPHA", Scalar("SELECT text FROM items WHERE id = 1"));
    }

    [TestMethod]
    public void OrWhereAndNonEmptyIn_BindLiteralValues()
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE items SET text = @text WHERE id = 1";
            command.Parameters.AddWithValue("@text", "!!'alpha' OR 1 = 1 --");
            command.ExecuteNonQuery();
        }
        var model = db.Update("items").SetValue("score", 99).WhereIn("text", new[] { "not present" })
            .OrWhere("text", "=", "!!'alpha' OR 1 = 1 --")
            .WhereNotIn("text", new[] { "!!'beta'" });
        Assert.AreEqual(1, model.ExecuteNonQuery());
        Assert.AreEqual(1L, Scalar("SELECT count(*) FROM items WHERE score = 99"));
    }

    [TestMethod]
    [DataRow("insert")]
    [DataRow("update")]
    public void MissingValues_AreRejectedBeforeExecution(string operation)
    {
        var model = operation == "insert" ? db.Insert("items") : db.Update("items");
        Assert.Throws<InvalidOperationException>(() => model.GenerateQuery());
        Assert.Throws<InvalidOperationException>(() => model.ExecuteNonQuery());
        Assert.AreEqual(3L, Scalar("SELECT count(*) FROM items"));
        Assert.AreEqual(60L, Scalar("SELECT sum(score) FROM items"));
    }

    public sealed class MappedRow
    {
        [Column("id")] public int Id { get; set; }
        [Column("text")] public string? Text { get; set; }
        [Column("score")] public int? Score { get; set; }
        [NotMapped] public string Ignored => "not a database column";
        public string _ignored => "also not a column";
    }

    public sealed class IndexerRow
    {
        public int id => 4;
        public string this[int index] => index.ToString();
    }

    public sealed class DuplicateRow
    {
        [Column("id")] public int First => 4;
        [Column("id")] public int Second => 5;
    }
}
