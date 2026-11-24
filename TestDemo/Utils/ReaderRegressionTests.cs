using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Daevsoft.Lib;

namespace TestDemo.Utils;

[TestClass]
public class ReaderRegressionTests
{
    private enum State : short { None = 0, Ready = 2 }

    private static DataTableReader Row(params object[] values)
    {
        var table = new DataTable();
        for (var i = 0; i < values.Length; i++)
            table.Columns.Add("Column" + i, typeof(object));
        table.Rows.Add(values);
        var reader = table.CreateDataReader();
        Assert.IsTrue(reader.Read());
        return reader;
    }

    [TestMethod]
    public void OrdinalsReadDistinctColumnsEvenWhenNamesAreDuplicated()
    {
        // DataTable forbids exact duplicate names; this adapter exposes the same real
        // ADO values with duplicate aliases, as a SQL join can return them.
        using var reader = new AliasedReader(Row(11, 22), "Id", "Id") { RejectSchema = false };
        var values = new ReaderUtil<IDataReader>(reader);
        Assert.AreEqual(11, values.GetValue<int>(0));
        Assert.AreEqual(22, values.GetValue<int>(1));
        Assert.AreEqual(11, values.GetValue<int>("ID"));
    }

    [TestMethod]
    public void OrdinalReadsDoNotRequireSchemaOrColumnNames()
    {
        using var reader = new AliasedReader(Row(42), "Value") { RejectNames = true };
        Assert.AreEqual(42, new ReaderUtil<IDataReader>(reader).GetValue<int>(0));
    }

    [TestMethod]
    public void ColumnNamesAreCachedCaseInsensitivelyAndReadOnce()
    {
        using var reader = new AliasedReader(Row(11, 22), "Id", "Total");
        var values = new ReaderUtil<IDataReader>(reader);
        Assert.IsTrue(values.IsColumnExist("TOTAL"));
        Assert.AreEqual(22, values.GetValue<int>("total"));
        Assert.AreEqual(11, values.GetValue<int>("iD"));
        Assert.IsFalse(values.IsColumnExist("missing"));
        Assert.AreEqual(2, reader.NameReads, "Names should be scanned once per result wrapper.");
        Assert.AreEqual(2, reader.ValueReads, "Each cell should be fetched only once.");
    }

    [TestMethod]
    public void MissingNamesAndInvalidOrdinalsThrowRatherThanReturningDefaults()
    {
        using var reader = Row(1);
        var values = new ReaderUtil<IDataReader>(reader);
        Assert.IsFalse(values.IsColumnExist("absent"));
        Assert.IsFalse(values.IsColumnExist(null!));
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => values.GetValue<int>("absent"));
        // Ordinal errors retain the provider's exception (DataTableReader uses this type).
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => values.GetValue<int>(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => values.GetValue<int>(1));
        Assert.ThrowsExactly<ArgumentNullException>(() => values.GetValue<int>((string)null!));
    }

    [TestMethod]
    public void NullableNumbersAndBooleansKeepTheirNativeValues()
    {
        using var reader = Row(12, 1.25m, true, (short)7, 123L, 2.5f, 1);
        var values = new ReaderUtil<IDataReader>(reader);
        Assert.AreEqual((int?)12, values.GetValue<int?>(0));
        Assert.AreEqual((decimal?)1.25m, values.GetValue<decimal?>(1));
        Assert.IsTrue(values.GetValue<bool?>(2));
        Assert.AreEqual((short?)7, values.GetValue<short?>(3));
        Assert.AreEqual((long?)123, values.GetValue<long?>(4));
        Assert.AreEqual((float?)2.5f, values.GetValue<float?>(5));
        Assert.IsTrue(values.GetValue<bool?>(6));
    }

    [TestMethod]
    public void DatabaseNullUsesNullableReferenceAndValueDefaults()
    {
        using var reader = Row(DBNull.Value);
        var values = new ReaderUtil<IDataReader>(reader);
        Assert.IsNull(values.GetValue<int?>(0));
        Assert.IsNull(values.GetValue<bool?>(0));
        Assert.IsNull(values.GetValue<Guid?>(0));
        Assert.IsNull(values.GetValue<State?>(0));
        Assert.IsNull(values.GetValue<byte[]>(0));
        Assert.IsNull(values.GetValue<object>(0));
        Assert.IsNull(values.GetValue<string>(0));
        Assert.AreEqual(0, values.GetValue<int>(0));
        Assert.IsFalse(values.GetValue<bool>(0));
        Assert.AreEqual(Guid.Empty, values.GetValue<Guid>(0));
        Assert.AreEqual(default(DateTime), values.GetValue<DateTime>(0));
        Assert.AreEqual(default(DateTimeOffset), values.GetValue<DateTimeOffset>(0));
        Assert.AreEqual(default(TimeSpan), values.GetValue<TimeSpan>(0));
    }

    [TestMethod]
    public void GuidAndBinaryValuesAreNotStringified()
    {
        var id = new Guid("5290e172-ae93-4d9c-a48c-fb60109bcc75");
        var bytes = new byte[] { 0, 1, 128, 255 };
        using var reader = Row(id, id.ToString(), id.ToByteArray(), bytes);
        var values = new ReaderUtil<IDataReader>(reader);
        Assert.AreEqual(id, values.GetValue<Guid>(0));
        Assert.AreEqual((Guid?)id, values.GetValue<Guid?>(1));
        Assert.AreEqual(id, values.GetValue<Guid>(2));
        CollectionAssert.AreEqual(bytes, values.GetValue<byte[]>(3));
        Assert.AreSame(bytes, values.GetValue<object>(3));
    }

    [TestMethod]
    public void EnumsSupportNamesNumbersAndNullableTargets()
    {
        using var reader = Row("ready", (short)2, State.Ready, "2");
        var values = new ReaderUtil<IDataReader>(reader);
        Assert.AreEqual(State.Ready, values.GetValue<State>(0));
        Assert.AreEqual((State?)State.Ready, values.GetValue<State?>(1));
        Assert.AreEqual(State.Ready, values.GetValue<State>(2));
        Assert.AreEqual(State.Ready, values.GetValue<State>(3));
    }

    [TestMethod]
    public void NumericConversionsUseNativeRoundingAndCheckOverflow()
    {
        using var reader = Row(12.75m, -12.75m, 2.5d, 65535, 256, 2.5f, (byte)3, 4U, 5UL, (sbyte)-6);
        var values = new ReaderUtil<IDataReader>(reader);
        Assert.AreEqual((short)13, values.GetValue<short>(0));
        Assert.AreEqual(-13L, values.GetValue<long>(1));
        Assert.AreEqual(2, values.GetValue<int>(2));
        Assert.AreEqual((ushort)65535, values.GetValue<ushort>(3));
        Assert.ThrowsExactly<OverflowException>(() => values.GetValue<byte>(4));
        Assert.AreEqual(2.5f, values.GetValue<float>(5));
        Assert.AreEqual((byte)3, values.GetValue<byte>(6));
        Assert.AreEqual(4U, values.GetValue<uint>(7));
        Assert.AreEqual(5UL, values.GetValue<ulong>(8));
        Assert.AreEqual((sbyte)-6, values.GetValue<sbyte>(9));
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("fr-FR")]
    [DataRow("de-DE")]
    public void TextAndNumericConversionsUseInvariantCulture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var reader = Row("1234.56", 1234.56m, "2024-03-15T12:30:00", "True", "A");
            var values = new ReaderUtil<IDataReader>(reader);
            Assert.AreEqual(1234.56m, values.GetValue<decimal>(0));
            Assert.AreEqual(1234.56d, values.GetValue<double>(0));
            Assert.AreEqual("1234.56", values.GetValue<string>(1));
            Assert.AreEqual(new DateTime(2024, 3, 15, 12, 30, 0), values.GetValue<DateTime>(2));
            Assert.IsTrue(values.GetValue<bool>(3));
            Assert.AreEqual('A', values.GetValue<char>(4));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [TestMethod]
    public void DateTimeOffsetAndTimeSpanSupportNativeAndTextValues()
    {
        var timestamp = new DateTimeOffset(2024, 3, 15, 12, 30, 0, TimeSpan.FromHours(2));
        var duration = new TimeSpan(1, 2, 3);
        var utc = new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Utc);
        using var reader = Row(timestamp, "2024-03-15T12:30:00+02:00", duration, "01:02:03", utc);
        var values = new ReaderUtil<IDataReader>(reader);
        Assert.AreEqual(timestamp, values.GetValue<DateTimeOffset>(0));
        Assert.AreEqual((DateTimeOffset?)timestamp, values.GetValue<DateTimeOffset?>(1));
        Assert.AreEqual(TimeSpan.FromHours(2), values.GetValue<DateTimeOffset>(1).Offset);
        Assert.AreEqual(duration, values.GetValue<TimeSpan>(2));
        Assert.AreEqual((TimeSpan?)duration, values.GetValue<TimeSpan?>(3));
        Assert.AreEqual(new DateTimeOffset(utc), values.GetValue<DateTimeOffset>(4));
    }

    [TestMethod]
    public void InvalidValuesAreNotSilentlyCoercedToDefaults()
    {
        using var reader = Row("not-a-number", "not-a-guid", "not-a-state", "not-binary");
        var values = new ReaderUtil<IDataReader>(reader);
        Assert.ThrowsExactly<FormatException>(() => values.GetValue<int>(0));
        Assert.ThrowsExactly<FormatException>(() => values.GetValue<Guid>(1));
        Assert.ThrowsExactly<ArgumentException>(() => values.GetValue<State>(2));
        Assert.ThrowsExactly<InvalidCastException>(() => values.GetValue<byte[]>(3));
    }

    [TestMethod]
    public void InterfaceOnlyReaderCanBeUsedWithoutDbDataReader()
    {
        using var source = Row(42);
        var proxy = System.Reflection.DispatchProxy.Create<IDataReader, InterfaceReader>();
        ((InterfaceReader)proxy).Source = source;
        // Reflection lets this regression compile before the IDataReader overload exists.
        var values = (ReaderUtil<IDataReader>)Activator.CreateInstance(typeof(ReaderUtil<IDataReader>), proxy)!;
        Assert.AreEqual(42, values.GetValue<int>(0));
        Assert.AreEqual(42, values.GetValue<int>("COLUMN0"));
    }

    [TestMethod]
    public void NullReaderIsRejectedAtConstruction()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ReaderUtil<IDataReader>((DbDataReader)null!));
    }

    public class InterfaceReader : System.Reflection.DispatchProxy
    {
        public IDataReader Source { get; set; } = null!;
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args) =>
            method!.Invoke(Source, args);
    }

    private sealed class AliasedReader : DbDataReader
    {
        private readonly DataTableReader inner;
        private readonly string[] names;
        public int NameReads { get; private set; }
        public int ValueReads { get; private set; }
        public bool RejectNames { get; set; }
        public bool RejectSchema { get; set; } = true;
        public AliasedReader(DataTableReader inner, params string[] names)
        {
            this.inner = inner;
            this.names = names;
        }
        public override string GetName(int ordinal)
        {
            NameReads++;
            if (RejectNames) throw new NotSupportedException("Column names are not available.");
            return names[ordinal];
        }
        public override object GetValue(int ordinal) { ValueReads++; return inner.GetValue(ordinal); }
        public override int GetOrdinal(string name)
        {
            var ordinal = Array.FindIndex(names, candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
            if (ordinal < 0) throw new IndexOutOfRangeException(name);
            return ordinal;
        }
        public override DataTable GetSchemaTable()
        {
            if (RejectSchema) throw new NotSupportedException("Column schema is not available.");
            var schema = inner.GetSchemaTable();
            for (var i = 0; i < names.Length; i++)
                schema.Rows[i]["ColumnName"] = names[i];
            return schema;
        }
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override int Depth => inner.Depth;
        public override bool Read() => inner.Read();
        public override bool NextResult() => inner.NextResult();
        public override void Close() => inner.Close();
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
