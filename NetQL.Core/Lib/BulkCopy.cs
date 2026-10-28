using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Daevsoft.Lib
{
    public sealed class BulkCopyColumn
    {
        private readonly Func<object, object> getter;

        internal BulkCopyColumn(string columnName, PropertyInfo property)
        {
            ColumnName = columnName;
            PropertyName = property.Name;
            ClrType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (ClrType.IsEnum) ClrType = Enum.GetUnderlyingType(ClrType);
            getter = CompileGetter(property);
        }

        public string ColumnName { get; }
        public string PropertyName { get; }
        public Type ClrType { get; }

        public object GetValue(object row)
        {
            var value = getter(row);
            if (value == null || value == DBNull.Value) return DBNull.Value;
            var type = value.GetType();
            if (type.IsEnum && !ClrType.IsEnum)
                return Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
            return value;
        }

        private static Func<object, object> CompileGetter(PropertyInfo property)
        {
            try
            {
                if (property.DeclaringType == null) return property.GetValue;
                var row = Expression.Parameter(typeof(object), "row");
                var body = Expression.Convert(
                    Expression.Property(Expression.Convert(row, property.DeclaringType), property), typeof(object));
                return Expression.Lambda<Func<object, object>>(body, row).Compile();
            }
            catch (Exception)
            {
                return property.GetValue;
            }
        }
    }

    public sealed class BulkCopyContext
    {
        private readonly Func<string, string> quoteIdentifier;

        internal BulkCopyContext(string tableName, IReadOnlyList<BulkCopyColumn> columns, int batchSize, int timeout,
            IDbConnection connection, IDbTransaction transaction, Func<string, string> quoteIdentifier)
        {
            TableName = tableName;
            Columns = columns;
            BatchSize = batchSize;
            Timeout = timeout;
            Connection = connection;
            Transaction = transaction;
            this.quoteIdentifier = quoteIdentifier;
        }

        public string TableName { get; }
        public IReadOnlyList<BulkCopyColumn> Columns { get; }
        public int BatchSize { get; }
        public int Timeout { get; }
        public IDbConnection Connection { get; }
        public IDbTransaction Transaction { get; }

        public string QuoteIdentifier(string name)
        {
            return quoteIdentifier(name);
        }

        internal IEnumerable<List<object>> Batches(IEnumerable<object> rows)
        {
            int capacity = Math.Min(Math.Max(BatchSize, 1), 1024);
            var batch = new List<object>(capacity);
            foreach (var row in rows)
            {
                batch.Add(row);
                if (batch.Count < BatchSize) continue;
                yield return batch;
                batch = new List<object>(capacity);
            }
            if (batch.Count > 0) yield return batch;
        }

        internal DataTable CreateBatch(List<object> batch)
        {
            var table = new DataTable();
            foreach (var column in Columns) table.Columns.Add(column.ColumnName, column.ClrType);
            foreach (var row in batch)
            {
                var values = new object[Columns.Count];
                for (int i = 0; i < values.Length; i++) values[i] = Columns[i].GetValue(row);
                table.Rows.Add(values);
            }
            return table;
        }
    }

    public interface IBulkCopyAdapter
    {
        long Write(BulkCopyContext context, IEnumerable<object> rows);
        Task<long> WriteAsync(BulkCopyContext context, IEnumerable<object> rows, CancellationToken cancellationToken);
    }

    internal static class BulkCopyMapping
    {
        internal static IReadOnlyList<BulkCopyColumn> Resolve(List<object> rows)
        {
            var type = rows[0].GetType();
            var columns = new List<BulkCopyColumn>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var columnName = property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name;
                if (property.GetCustomAttribute<NotMappedAttribute>() != null ||
                    property.Name.StartsWith("_", StringComparison.Ordinal) || columnName.StartsWith("_", StringComparison.Ordinal))
                    continue;
                if (property.GetIndexParameters().Length != 0 || property.GetGetMethod() == null)
                    throw new ArgumentException("Bulk copy rows must contain readable, non-indexed properties.", nameof(rows));
                if (string.IsNullOrWhiteSpace(columnName) || !names.Add(columnName))
                    throw new ArgumentException("Bulk copy column names must be non-empty and unique.", nameof(rows));
                columns.Add(new BulkCopyColumn(columnName, property));
            }
            if (columns.Count == 0)
                throw new ArgumentException("Bulk copy rows must contain at least one mapped column.", nameof(rows));
            for (int i = 1; i < rows.Count; i++)
                if (!type.IsInstanceOfType(rows[i]))
                    throw new ArgumentException("All bulk copy rows must be the same mapped type.", nameof(rows));
            return columns;
        }
    }

    internal static class BulkCopyReflection
    {
        internal static void Set(object target, string propertyName, object value)
        {
            var property = target.GetType().GetProperty(propertyName);
            if (property == null || !property.CanWrite || value == null) return;
            property.SetValue(target, value);
        }

        internal static void Invoke(object target, string methodName)
        {
            var method = target.GetType().GetMethod(methodName, Type.EmptyTypes);
            if (method == null) throw new NotSupportedException(target.GetType().FullName + " does not expose " + methodName + ".");
            method.Invoke(target, null);
        }
    }

    internal sealed class SqlServerBulkCopyAdapter : IBulkCopyAdapter
    {
        internal static readonly SqlServerBulkCopyAdapter Instance = new SqlServerBulkCopyAdapter();

        public long Write(BulkCopyContext context, IEnumerable<object> rows)
        {
            var bulk = Create(context, out var type);
            try
            {
                Configure(bulk, type, context);
                var write = type.GetMethod("WriteToServer", new[] { typeof(DataTable) });
                if (write == null) throw new NotSupportedException(type.FullName + " does not expose WriteToServer(DataTable).");
                long total = 0;
                foreach (var batch in context.Batches(rows))
                {
                    using (var table = context.CreateBatch(batch))
                    {
                        write.Invoke(bulk, new object[] { table });
                        total += batch.Count;
                    }
                }
                return total;
            }
            finally
            {
                (bulk as IDisposable)?.Dispose();
            }
        }

        public async Task<long> WriteAsync(BulkCopyContext context, IEnumerable<object> rows, CancellationToken cancellationToken)
        {
            var bulk = Create(context, out var type);
            try
            {
                Configure(bulk, type, context);
                var write = type.GetMethod("WriteToServerAsync", new[] { typeof(DataTable), typeof(CancellationToken) })
                    ?? type.GetMethod("WriteToServerAsync", new[] { typeof(DataTable) });
                if (write == null) throw new NotSupportedException(type.FullName + " does not expose WriteToServerAsync(DataTable).");
                long total = 0;
                foreach (var batch in context.Batches(rows))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var table = context.CreateBatch(batch))
                    {
                        var arguments = write.GetParameters().Length == 2
                            ? new object[] { table, cancellationToken }
                            : new object[] { table };
                        var task = write.Invoke(bulk, arguments) as Task;
                        if (task == null) throw new NotSupportedException(type.FullName + ".WriteToServerAsync did not return a Task.");
                        await task.ConfigureAwait(false);
                        total += batch.Count;
                    }
                }
                return total;
            }
            finally
            {
                (bulk as IDisposable)?.Dispose();
            }
        }

        private static object Create(BulkCopyContext context, out Type type)
        {
            var connectionType = context.Connection.GetType();
            type = connectionType.Assembly.GetType(connectionType.FullName.StartsWith("Microsoft.", StringComparison.Ordinal)
                ? "Microsoft.Data.SqlClient.SqlBulkCopy"
                : "System.Data.SqlClient.SqlBulkCopy", false);
            if (type == null) throw new NotSupportedException("SqlBulkCopy is not available for " + connectionType.FullName + ".");
            var optionsType = type.Assembly.GetType(type.Namespace + ".SqlBulkCopyOptions");
            var options = optionsType == null ? (object)0 : Enum.ToObject(optionsType, 0);
            return Activator.CreateInstance(type, context.Connection, options, context.Transaction);
        }

        private static void Configure(object bulk, Type type, BulkCopyContext context)
        {
            BulkCopyReflection.Set(bulk, "DestinationTableName", context.TableName);
            if (context.Timeout > 0) BulkCopyReflection.Set(bulk, "BulkCopyTimeout", context.Timeout);
            BulkCopyReflection.Set(bulk, "BatchSize", context.BatchSize);
            var mappings = type.GetProperty("ColumnMappings")?.GetValue(bulk);
            var add = mappings?.GetType().GetMethod("Add", new[] { typeof(string), typeof(string) });
            if (add == null) return;
            foreach (var column in context.Columns)
                add.Invoke(mappings, new object[] { column.ColumnName, column.ColumnName });
        }
    }

    internal sealed class MySqlBulkCopyAdapter : IBulkCopyAdapter
    {
        internal static readonly MySqlBulkCopyAdapter Instance = new MySqlBulkCopyAdapter();

        public long Write(BulkCopyContext context, IEnumerable<object> rows)
        {
            var bulk = Create(context, out var type);
            try
            {
                Configure(bulk, type, context);
                var write = type.GetMethod("WriteToServer", new[] { typeof(DataTable) });
                if (write == null) throw new NotSupportedException(type.FullName + " does not expose WriteToServer(DataTable).");
                long total = 0;
                foreach (var batch in context.Batches(rows))
                {
                    using (var table = context.CreateBatch(batch))
                    {
                        write.Invoke(bulk, new object[] { table });
                        total += batch.Count;
                    }
                }
                return total;
            }
            finally
            {
                (bulk as IDisposable)?.Dispose();
            }
        }

        public async Task<long> WriteAsync(BulkCopyContext context, IEnumerable<object> rows, CancellationToken cancellationToken)
        {
            var bulk = Create(context, out var type);
            try
            {
                Configure(bulk, type, context);
                var write = type.GetMethod("WriteToServerAsync", new[] { typeof(DataTable), typeof(CancellationToken) })
                    ?? type.GetMethod("WriteToServerAsync", new[] { typeof(DataTable) });
                if (write == null) throw new NotSupportedException(type.FullName + " does not expose WriteToServerAsync(DataTable).");
                long total = 0;
                foreach (var batch in context.Batches(rows))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var table = context.CreateBatch(batch))
                    {
                        var arguments = write.GetParameters().Length == 2
                            ? new object[] { table, cancellationToken }
                            : new object[] { table };
                        var task = write.Invoke(bulk, arguments) as Task;
                        if (task == null) throw new NotSupportedException(type.FullName + ".WriteToServerAsync did not return a Task.");
                        await task.ConfigureAwait(false);
                        total += batch.Count;
                    }
                }
                return total;
            }
            finally
            {
                (bulk as IDisposable)?.Dispose();
            }
        }

        private static object Create(BulkCopyContext context, out Type type)
        {
            var connectionType = context.Connection.GetType();
            if (connectionType.FullName != "MySqlConnector.MySqlConnection")
                throw new NotSupportedException("MySqlBulkCopy requires MySqlConnector. Use the SQL bulk fallback for other MySQL drivers.");
            type = connectionType.Assembly.GetType("MySqlConnector.MySqlBulkCopy", false);
            if (type == null) throw new NotSupportedException("MySqlBulkCopy is not available in this MySqlConnector version.");
            if (context.Transaction == null)
            {
                var single = type.GetConstructor(new[] { connectionType });
                if (single != null) return single.Invoke(new object[] { context.Connection });
            }
            var constructor = type.GetConstructor(new[] { connectionType, context.Transaction == null
                ? connectionType.Assembly.GetType("MySqlConnector.MySqlTransaction")
                : context.Transaction.GetType() });
            if (constructor == null)
                throw new NotSupportedException("MySqlBulkCopy in this MySqlConnector version cannot enlist in a transaction.");
            return constructor.Invoke(new object[] { context.Connection, context.Transaction });
        }

        private static void Configure(object bulk, Type type, BulkCopyContext context)
        {
            BulkCopyReflection.Set(bulk, "DestinationTableName", context.TableName);
            if (context.Timeout > 0) BulkCopyReflection.Set(bulk, "BulkCopyTimeout", context.Timeout);
            var mappings = type.GetProperty("ColumnMappings")?.GetValue(bulk);
            if (mappings == null) return;
            var add = mappings.GetType().GetMethod("Add");
            var mappingType = add?.GetParameters()[0].ParameterType;
            if (add == null || mappingType == null) return;
            foreach (var column in context.Columns)
            {
                var mapping = Activator.CreateInstance(mappingType, column.ColumnName, column.ColumnName);
                add.Invoke(mappings, new[] { mapping });
            }
        }
    }

    internal sealed class OracleBulkCopyAdapter : IBulkCopyAdapter
    {
        internal static readonly OracleBulkCopyAdapter Instance = new OracleBulkCopyAdapter();

        public long Write(BulkCopyContext context, IEnumerable<object> rows)
        {
            if (context.Transaction != null)
                throw new NotSupportedException("OracleBulkCopy cannot enlist in a caller-managed transaction. Use Insert(...).Bulk(rows, chunkSize) for transactional loads.");
            var bulk = Create(context, out var type);
            try
            {
                BulkCopyReflection.Set(bulk, "DestinationTableName", context.TableName);
                if (context.Timeout > 0) BulkCopyReflection.Set(bulk, "BulkCopyTimeout", context.Timeout);
                var write = type.GetMethod("WriteToServer", new[] { typeof(DataTable) })
                    ?? type.GetMethod("WriteToServer", new[] { typeof(IDataReader) });
                if (write == null) throw new NotSupportedException(type.FullName + " does not expose WriteToServer.");
                bool fromReader = write.GetParameters()[0].ParameterType != typeof(DataTable);
                long total = 0;
                foreach (var batch in context.Batches(rows))
                {
                    using (var table = context.CreateBatch(batch))
                    {
                        write.Invoke(bulk, new object[] { fromReader ? (object)table.CreateDataReader() : table });
                        total += batch.Count;
                    }
                }
                return total;
            }
            finally
            {
                (bulk as IDisposable)?.Dispose();
            }
        }

        public Task<long> WriteAsync(BulkCopyContext context, IEnumerable<object> rows, CancellationToken cancellationToken)
        {
            // OracleBulkCopy has no asynchronous API; the returned task completes after the synchronous load.
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Write(context, rows));
        }

        private static object Create(BulkCopyContext context, out Type type)
        {
            var connectionType = context.Connection.GetType();
            if (connectionType.FullName != "Oracle.ManagedDataAccess.Client.OracleConnection")
                throw new NotSupportedException("OracleBulkCopy requires Oracle.ManagedDataAccess.");
            type = connectionType.Assembly.GetType("Oracle.ManagedDataAccess.Client.OracleBulkCopy", false);
            if (type == null) throw new NotSupportedException("OracleBulkCopy is not available.");
            return Activator.CreateInstance(type, context.Connection);
        }
    }

    internal sealed class PostgreSqlBulkCopyAdapter : IBulkCopyAdapter
    {
        internal static readonly PostgreSqlBulkCopyAdapter Instance = new PostgreSqlBulkCopyAdapter();

        public long Write(BulkCopyContext context, IEnumerable<object> rows)
        {
            var importer = Begin(context);
            var type = importer.GetType();
            try
            {
                var startRow = type.GetMethod("StartRow", Type.EmptyTypes);
                var writeNull = type.GetMethod("WriteNull", Type.EmptyTypes);
                var write = FindWrite(type, false);
                if (startRow == null || writeNull == null || write == null)
                    throw new NotSupportedException("This Npgsql version does not expose the binary import API used by NetQL.");
                long total = 0;
                foreach (var row in rows)
                {
                    startRow.Invoke(importer, null);
                    foreach (var column in context.Columns)
                        WriteValue(importer, write, writeNull, column.GetValue(row), column);
                    total++;
                }
                BulkCopyReflection.Invoke(importer, "Complete");
                return total;
            }
            finally
            {
                (importer as IDisposable)?.Dispose();
            }
        }

        public async Task<long> WriteAsync(BulkCopyContext context, IEnumerable<object> rows, CancellationToken cancellationToken)
        {
            var importer = await BeginAsync(context, cancellationToken).ConfigureAwait(false);
            var type = importer.GetType();
            try
            {
                var startRow = type.GetMethod("StartRowAsync", new[] { typeof(CancellationToken) });
                var writeNull = type.GetMethod("WriteNullAsync", new[] { typeof(CancellationToken) });
                var write = FindWrite(type, true);
                var complete = type.GetMethod("CompleteAsync", new[] { typeof(CancellationToken) })
                    ?? type.GetMethod("CompleteAsync", Type.EmptyTypes);
                if (startRow == null || write == null || complete == null)
                    throw new NotSupportedException("This Npgsql version does not expose the asynchronous binary import API used by NetQL.");
                long total = 0;
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await InvokeAsync(startRow, importer, new object[] { cancellationToken }).ConfigureAwait(false);
                    foreach (var column in context.Columns)
                    {
                        var value = column.GetValue(row);
                        if (value != DBNull.Value)
                        {
                            await WriteValueAsync(importer, write, value, column, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        if (writeNull != null) await InvokeAsync(writeNull, importer, new object[] { cancellationToken }).ConfigureAwait(false);
                        else BulkCopyReflection.Invoke(importer, "WriteNull");
                    }
                    total++;
                }
                await InvokeAsync(complete, importer, complete.GetParameters().Length == 0
                    ? new object[0] : new object[] { cancellationToken }).ConfigureAwait(false);
                return total;
            }
            finally
            {
                if (importer is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else
                    (importer as IDisposable)?.Dispose();
            }
        }

        private static object Begin(BulkCopyContext context)
        {
            var connectionType = context.Connection.GetType();
            if (connectionType.FullName != "Npgsql.NpgsqlConnection")
                throw new NotSupportedException("The PostgreSQL bulk copy adapter requires Npgsql.");
            var begin = connectionType.GetMethod("BeginBinaryImport", new[] { typeof(string) });
            if (begin == null) throw new NotSupportedException("This Npgsql version does not expose BeginBinaryImport.");
            return begin.Invoke(context.Connection, new object[] { CopyCommand(context) });
        }

        private static async Task<object> BeginAsync(BulkCopyContext context, CancellationToken cancellationToken)
        {
            var connectionType = context.Connection.GetType();
            if (connectionType.FullName != "Npgsql.NpgsqlConnection")
                throw new NotSupportedException("The PostgreSQL bulk copy adapter requires Npgsql.");
            var begin = connectionType.GetMethod("BeginBinaryImportAsync", new[] { typeof(string), typeof(CancellationToken) });
            if (begin == null) return Begin(context);
            var task = begin.Invoke(context.Connection, new object[] { CopyCommand(context), cancellationToken }) as Task;
            if (task == null) throw new NotSupportedException("BeginBinaryImportAsync did not return a Task.");
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result").GetValue(task);
        }

        private static string CopyCommand(BulkCopyContext context)
        {
            return "COPY " + context.TableName + " (" +
                string.Join(",", context.Columns.Select(column => context.QuoteIdentifier(column.ColumnName))) +
                ") FROM STDIN (FORMAT BINARY)";
        }

        private static MethodInfo FindWrite(Type importer, bool async)
        {
            var candidates = importer.GetMethods().Where(method => method.Name == (async ? "WriteAsync" : "Write")).ToArray();
            if (async)
            {
                var typed = candidates.FirstOrDefault(method => method.GetParameters().Length == 3
                    && method.GetParameters()[2].ParameterType == typeof(CancellationToken)
                    && IsTypeParameter(method.GetParameters()[1].ParameterType));
                if (typed != null) return typed;
                return candidates.FirstOrDefault(method => method.GetParameters().Length == 2
                    && method.GetParameters()[1].ParameterType == typeof(CancellationToken));
            }
            var typedSync = candidates.FirstOrDefault(method => method.GetParameters().Length == 2
                && IsTypeParameter(method.GetParameters()[1].ParameterType));
            return typedSync ?? candidates.FirstOrDefault(method => method.GetParameters().Length == 1);
        }

        private static bool IsTypeParameter(Type type)
        {
            return type == typeof(DbType) || type.IsEnum;
        }

        private static void WriteValue(object importer, MethodInfo method, MethodInfo writeNull, object value, BulkCopyColumn column)
        {
            if (value == DBNull.Value)
            {
                writeNull.Invoke(importer, null);
                return;
            }
            method = Close(method, value);
            var parameters = method.GetParameters();
            method.Invoke(importer, parameters.Length == 1
                ? new[] { value }
                : new[] { value, TypeValue(parameters[1].ParameterType, column.ClrType) });
        }

        private static async Task WriteValueAsync(object importer, MethodInfo method, object value, BulkCopyColumn column, CancellationToken cancellationToken)
        {
            method = Close(method, value);
            var parameters = method.GetParameters();
            var arguments = parameters.Length == 2
                ? new[] { value, (object)cancellationToken }
                : new[] { value, TypeValue(parameters[1].ParameterType, column.ClrType), (object)cancellationToken };
            var task = method.Invoke(importer, arguments) as Task;
            if (task == null) throw new NotSupportedException(method.Name + " did not return a Task.");
            await task.ConfigureAwait(false);
        }

        private static MethodInfo Close(MethodInfo method, object value)
        {
            return method.IsGenericMethodDefinition ? method.MakeGenericMethod(value.GetType()) : method;
        }

        private static object TypeValue(Type parameterType, Type clrType)
        {
            if (parameterType == typeof(DbType)) return DbTypeValue(clrType);
            return Enum.Parse(parameterType, PostgreSqlType(clrType), true);
        }

        private static DbType DbTypeValue(Type clrType)
        {
            var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
            if (type.IsEnum) type = Enum.GetUnderlyingType(type);
            if (type == typeof(bool)) return DbType.Boolean;
            if (type == typeof(byte[])) return DbType.Binary;
            if (type == typeof(Guid)) return DbType.Guid;
            if (type == typeof(DateTime)) return DbType.DateTime;
            if (type == typeof(DateTimeOffset)) return DbType.DateTimeOffset;
            if (type == typeof(TimeSpan)) return DbType.Time;
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16: return DbType.Int16;
                case TypeCode.Int32:
                case TypeCode.UInt32: return DbType.Int32;
                case TypeCode.Int64:
                case TypeCode.UInt64: return DbType.Int64;
                case TypeCode.Single: return DbType.Single;
                case TypeCode.Double: return DbType.Double;
                case TypeCode.Decimal: return DbType.Decimal;
                default: return DbType.String;
            }
        }

        private static string PostgreSqlType(Type clrType)
        {
            var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
            if (type.IsEnum) type = Enum.GetUnderlyingType(type);
            if (type == typeof(bool)) return "Boolean";
            if (type == typeof(byte[])) return "Bytea";
            if (type == typeof(Guid)) return "Uuid";
            if (type == typeof(DateTimeOffset)) return "Timestamptz";
            if (type == typeof(TimeSpan)) return "Interval";
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16: return "Smallint";
                case TypeCode.Int32:
                case TypeCode.UInt32: return "Integer";
                case TypeCode.Int64:
                case TypeCode.UInt64: return "Bigint";
                case TypeCode.Single: return "Real";
                case TypeCode.Double: return "Double";
                case TypeCode.Decimal: return "Numeric";
                case TypeCode.DateTime: return "Timestamp";
                default: return "Text";
            }
        }

        private static async Task InvokeAsync(MethodInfo method, object target, object[] arguments)
        {
            var task = method.Invoke(target, arguments) as Task;
            if (task == null) throw new NotSupportedException(method.Name + " did not return a Task.");
            await task.ConfigureAwait(false);
        }
    }
}
