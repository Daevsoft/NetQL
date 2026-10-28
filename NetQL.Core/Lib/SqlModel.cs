using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using System.Threading.Tasks;

namespace Daevsoft.Lib
{
    enum SqlModelType { INSERT, UPDATE, DELETE }
    public class SqlModel : QueryCommon
    {
        private string query;
        private SqlModelType sqlModelType;
        private List<Set> columnValues;
        private DbUtils dbUtils;
        private List<SetWhere> whereValues;
        private List<List<Set>> bulkInsertData;
        private readonly List<Set> additionalParameters = new List<Set>();
        private int executionStarted;
        private int bulkChunkSize;
        private bool bulkCommitEachChunk;
        protected new string FixBindName(string columnName, string additional = "")
        {
            return "write_" + base.FixBindName(columnName, additional + "_" + columnValues.Count);
        }
        public SqlModel(DbUtils dbUtils, string quotSql, string endQuotSql, string bindSymbol)
        {
            this.dbUtils = dbUtils;
            this.quotSql = quotSql;
            this.endQuotSql = endQuotSql;
            this.bindSymbol = bindSymbol;
            columnValues = new List<Set>();
            whereValues = new List<SetWhere>();
            bulkInsertData = new List<List<Set>>();
        }
        private List<Set> ReadRow(object data)
        {
            if (data == null || data is IEnumerable)
                throw new ArgumentException("A row must be a non-null object, not a collection or string.", nameof(data));

            var result = new List<Set>();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in data.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var columnName = prop.GetCustomAttribute<ColumnAttribute>()?.Name ?? prop.Name;
                if (prop.GetCustomAttribute<NotMappedAttribute>() != null ||
                    prop.Name.StartsWith("_", StringComparison.Ordinal) || columnName.StartsWith("_", StringComparison.Ordinal))
                    continue;
                if (prop.GetIndexParameters().Length != 0 || prop.GetGetMethod() == null)
                    throw new ArgumentException("Rows must contain readable, non-indexed properties.", nameof(data));
                if (string.IsNullOrWhiteSpace(columnName) || !columns.Add(columnName))
                    throw new ArgumentException("Row column names must be non-empty and unique.", nameof(data));
                result.Add(new Set { Column = columnName, Value = prop.GetValue(data), VType = GetType(prop.PropertyType) });
            }
            if (result.Count == 0)
                throw new ArgumentException("A row must contain at least one mapped column.", nameof(data));
            return result;
        }
        internal SqlModel BindRow(object data) => SetBulk(data);

        private SqlModel SetBulk(object data)
        {
            var values = ReadRow(data);
            foreach (var value in values)
                SetValue(value.Column, value.Value, value.VType);
            return this;
        }
        public SqlModel Bulk(object dataBulk)
        {
            return Bulk(dataBulk, 0);
        }

        public SqlModel Bulk(object dataBulk, int chunkSize, bool commitEachChunk = false)
        {
            if (chunkSize < 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
            if (commitEachChunk && chunkSize <= 0)
                throw new ArgumentException("Chunked bulk writes require a positive chunk size.", nameof(chunkSize));
            if (sqlModelType == SqlModelType.UPDATE)
            {
                if (chunkSize > 0 || commitEachChunk)
                    throw new InvalidOperationException("Chunked bulk writes are only supported for inserts.");
                return SetBulk(dataBulk);
            }
            if (sqlModelType != SqlModelType.INSERT)
                throw new InvalidOperationException("Bulk data is only supported for inserts and updates.");
            if (columnValues.Count != 0)
                throw new InvalidOperationException("Bulk rows cannot be combined with explicit column values.");

            // Validate and snapshot the entire batch before changing the builder or touching a command.
            var rows = new List<List<Set>>();
            if (dataBulk is IEnumerable items && !(dataBulk is string))
            {
                foreach (var data in items)
                    rows.Add(ReadRow(data));
            }
            else
                rows.Add(ReadRow(dataBulk));
            if (rows.Count == 0)
                throw new ArgumentException("Bulk data must contain at least one row.", nameof(dataBulk));

            var first = bulkInsertData.Count == 0 ? rows[0] : bulkInsertData[0];
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                if (row.Count != first.Count || first.Any(column =>
                    !row.Any(value => string.Equals(value.Column, column.Column, StringComparison.OrdinalIgnoreCase))))
                    throw new ArgumentException("All bulk rows must have the same mapped columns.", nameof(dataBulk));

                rows[rowIndex] = first.Select(column => row.Single(value =>
                    string.Equals(value.Column, column.Column, StringComparison.OrdinalIgnoreCase))).ToList();
                for (int columnIndex = 0; columnIndex < first.Count; columnIndex++)
                    rows[rowIndex][columnIndex].BindName = "write_bulk_" + (bulkInsertData.Count + rowIndex) + "_" + columnIndex;
            }
            bulkInsertData.AddRange(rows);
            bulkChunkSize = chunkSize;
            bulkCommitEachChunk = commitEachChunk;
            return this;
        }
        internal SqlModel BulkRows(object dataBulk, int chunkSize, bool commitEachChunk = false)
        {
            return Bulk(dataBulk, chunkSize, commitEachChunk);
        }
        public SqlModel Insert(string tableName)
        {
            sqlModelType = SqlModelType.INSERT;
            query = "INSERT INTO " + WrapQuot(tableName);
            return this;
        }
        public SqlModel Update(string tableName)
        {
            sqlModelType = SqlModelType.UPDATE;
            query = "UPDATE " + WrapQuot(tableName);
            return this;
        }
        public SqlModel Delete(string tableName)
        {
            sqlModelType = SqlModelType.DELETE;
            query = "DELETE FROM " + WrapQuot(tableName);
            return this;
        }
        public SqlModel WhereIn(string columnName, Func<DbUtils, DbUtils> subQuery)
        {
            return Where(columnName, "IN", subQuery);
        }
        public SqlModel WhereNotIn(string columnName, Func<DbUtils, DbUtils> subQuery)
        {
            return Where(columnName, "NOT IN", subQuery);
        }
        public SqlModel WhereNotIn<B>(string columnName, B[] values)
        {
            return InList(columnName, values, "NOT IN", "AND");
        }
        public SqlModel OrWhereNotIn(string columnName, Func<DbUtils, DbUtils> subQuery)
        {
            return Where(columnName, "NOT IN", subQuery, "OR");
        }
        public SqlModel OrWhereNotIn<B>(string columnName, B[] values)
        {
            return InList(columnName, values, "NOT IN", "OR");
        }
        public SqlModel WhereIn<B>(string columnName, B[] values)
        {
            return InList(columnName, values, "IN", "AND");
        }
        public SqlModel OrWhereIn(string columnName, Func<DbUtils, DbUtils> subQuery)
        {
            return Where(columnName, "IN", subQuery, "OR");
        }
        public SqlModel OrWhereIn<B>(string columnName, B[] values)
        {
            return InList(columnName, values, "IN", "OR");
        }
        private SqlModel InList<B>(string columnName, B[] values, string valueOperator, string operatorKeyword)
        {
            if (values == null || values.Length == 0)
            {
                whereValues.Add(new SetWhereRaw
                {
                    Operator = operatorKeyword,
                    ValueOperator = string.Empty,
                    Value = valueOperator == "IN" ? "1 = 0" : "1 = 1",
                    IsRaw = true
                });
                return this;
            }

            var items = new List<Set>();
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                items.Add(new Set
                {
                    Column = columnName,
                    BindName = FixBindName(columnName, "_" + identity + "_" + whereValues.Count + "_" + i),
                    Value = value,
                    VType = GetType(value),
                });
            }
            whereValues.Add(new SetWhereIn
            {
                Column = columnName,
                Operator = operatorKeyword,
                ValueOperator = valueOperator,
                Items = items,
            });
            return this;
        }
        public SqlModel WhereRaw(string whereQuery)
        {
            whereValues.Add(new SetWhereRaw { Column = null, BindName = null, Value = whereQuery, ValueOperator = string.Empty, IsRaw = true });
            return this;
        }
        private int identity = 0;
        public DbUtils Clone()
        {
            identity++;
            return dbUtils.Clone();
        }
        public SqlModel OrWhere(object wheres)
        {
            if (wheres == null) throw new ArgumentNullException(nameof(wheres));
            if (wheres is Array)
            {
                throw new Exception("Bulk Update it's not support for array data");
            }
            var properties = wheres.GetType().GetProperties().AsEnumerable();
            int propertiesLength = properties.Count();

            for (int i = 0; i < propertiesLength; i++)
            {
                var prop = properties.ElementAt(i);

                var valueProp = prop.GetValue(wheres);
                var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var columnName = columnAttr != null ? columnAttr.Name : prop.Name;
                if (valueProp == null)
                {
                    OrNull(columnName);
                    continue;
                }
                else
                {
                    OrWhere(columnName, valueProp);
                }
            }
            return this;
        }
        public SqlModel Wrap(Func<DbUtils, DbUtils> wheres)
        {
            var dbClone = Clone();
            var subQuery = DbUtils.ValidateSubquery(dbClone, wheres(dbClone));

            whereValues.Add(new SubWhere
            {
                Column = null,
                Operator = "AND",
                ValueOperator = string.Empty,
                Value = "(" + subQuery.GenerateWhere(true) + ")",
                SubDbUtil = subQuery,
                IsRaw = true,
            });
            return this;
        }
        public SqlModel OrWrap(Func<DbUtils, DbUtils> wheres)
        {
            var dbClone = Clone();
            var subQuery = DbUtils.ValidateSubquery(dbClone, wheres(dbClone));

            whereValues.Add(new SubWhere
            {
                Column = null,
                Operator = "OR",
                ValueOperator = string.Empty,
                Value = "(" + subQuery.GenerateWhere(true) + ")",
                SubDbUtil = subQuery,
                IsRaw = true,
            });
            return this;
        }
        public SqlModel Where(object wheres)
        {
            if (wheres == null) throw new ArgumentNullException(nameof(wheres));
            if (wheres is Array)
            {
                throw new Exception("Bulk Update it's not support for array data");
            }
            var properties = wheres.GetType().GetProperties().AsEnumerable();
            int propertiesLength = properties.Count();

            for (int i = 0; i < propertiesLength; i++)
            {
                var prop = properties.ElementAt(i);
                var valueProp = prop.GetValue(wheres);
                var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var columnName = columnAttr != null ? columnAttr.Name : prop.Name;
                if (valueProp == null)
                {
                    AndNull(columnName);
                    continue;
                }
                else
                {
                    Where(columnName, valueProp);
                }
            }
            return this;
        }
        public SqlModel AndNull(string columnName)
        {
            return WhereRaw(columnName, " IS ", "NULL");
        }
        public SqlModel OrNull(string columnName)
        {
            return OrWhereRaw(columnName, " IS ", "NULL");
        }

        public SqlModel Where(string columnName, object value)
        {
            if (value == null)
            {
                return AndNull(columnName);
            }
            else
            {
                return Where(columnName, value, null);
            }
        }
        public SqlModel OrWhere(string columnName, object value)
        {
            if (value == null)
            {
                return OrNull(columnName);
            }
            else
            {
                return OrWhere(columnName, value, null);
            }
        }
        public SqlModel Where(string columnName, string valueOperator, Func<DbUtils, DbUtils> wheres, string oOperator = "AND")
        {
            var dbClone = Clone();
            var subQuery = DbUtils.ValidateSubquery(dbClone, wheres(dbClone));
            // set default operator WHERE
            if (whereValues.Count == 0) oOperator = "WHERE";

            whereValues.Add(new SubWhere
            {
                Column = columnName,
                Operator = oOperator,
                ValueOperator = valueOperator,
                Value = "(" + subQuery.GenerateQuery() + ")",
                SubDbUtil = subQuery
            });
            return this;
        }
        public SqlModel Where<B>(string columnName, B value, Func<string, string> customBind)
        {
            string bindName = FixBindName(columnName, "_" + identity + "_" + whereValues.Count);
            whereValues.Add(new SetWhere { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind });
            return this;
        }
        public SqlModel Where<B>(string columnName, B value)
        {
            Where(columnName, value, null);
            return this;
        }
        public SqlModel Where<B>(string columnName, string oOperator, B value, Func<string, string> customBind = null)
        {
            string bindName = FixBindName(columnName, "_" + identity + "_" + whereValues.Count);
            whereValues.Add(new SetWhere { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind, ValueOperator = oOperator });
            return this;
        }
        public SqlModel OrWhere<B>(string columnName, B value, Func<string, string> customBind = null)
        {
            string bindName = FixBindName(columnName, "_" + identity + "_" + whereValues.Count);
            whereValues.Add(new SetWhere { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR" });
            return this;
        }
        public SqlModel OrWhere<B>(string columnName, string oOperator, B value, Func<string, string> customBind = null)
        {
            string bindName = FixBindName(columnName, "_" + identity + "_" + whereValues.Count);
            whereValues.Add(new SetWhere { Column = columnName, BindName = bindName, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR", ValueOperator = oOperator });
            return this;
        }
        public SqlModel OrWhereRaw(string columnName, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = columnName, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR", IsRaw = true });
            return this;
        }
        public SqlModel OrWhereRaw(string columnName, string oOperator, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = columnName, Value = value, VType = GetType(value), CustomBind = customBind, Operator = "OR", ValueOperator = oOperator, IsRaw = true });
            return this;
        }
        public SqlModel WhereRaw(string columnName, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = columnName, Value = value, VType = GetType(value), CustomBind = customBind, IsRaw = true });
            return this;
        }
        public SqlModel WhereRaw(string columnName, string oOperator, object value, Func<string, string> customBind = null)
        {
            whereValues.Add(new SetWhereRaw { Column = columnName, BindName = columnName, Value = value, VType = GetType(value), CustomBind = customBind, IsRaw = true, ValueOperator = oOperator });
            return this;
        }
        public SqlModel AddValue(string columnName, object value, Func<string, string> customBind = null)
        {
            AddValue(columnName, value, GetType(value), customBind);
            return this;
        }
        public SqlModel AddValue(string columnName, object value, DbType dbType, Func<string, string> customBind = null)
        {
            if (bulkInsertData.Count != 0)
                throw new InvalidOperationException("Bulk rows cannot be combined with explicit column values.");
            string bindName = FixBindName(columnName, "_" + identity + "_" + whereValues.Count);
            columnValues.Add(new Set { Column = columnName, BindName = bindName, Value = value, VType = dbType, CustomBind = customBind });
            return this;
        }
        public SqlModel AddRawValue(string columnName, string value)
        {
            if (bulkInsertData.Count != 0)
                throw new InvalidOperationException("Bulk rows cannot be combined with explicit column values.");
            columnValues.Add(new SetRaw { Column = columnName, Value = value ?? "NULL", IsRaw = true });
            return this;
        }
        public SqlModel SetValue<B>(string columnName, B value, Func<string, string> customBind = null)
        {
            AddValue(columnName, value, GetType(value), customBind);
            return this;
        }
        public SqlModel SetValue(string columnName, object value, DbType dbType, Func<string, string> customBind = null)
        {
            AddValue(columnName, value, dbType, customBind);
            return this;
        }
        public SqlModel SetRawValue(string columnName, string value)
        {
            if (value == null)
                SetRawValue(columnName, "NULL");
            else
                AddRawValue(columnName, value);
            return this;
        }
        public dynamic Commit(bool force = true)
        {
            return dbUtils.Commit(force);
        }
        public SqlModel AddParameter(string name, object value, DbType type = DbType.String)
        {
            // Query resets DbUtils parameters, so keep these on the write builder until preparation.
            additionalParameters.Add(new Set { BindName = name, Value = value, VType = type });
            return this;
        }
        public dynamic Execute(Action<dynamic> callback = null)
        {
            if (bulkChunkSize > 0)
            {
                callback?.Invoke(ExecuteChunks());
                return null;
            }
            PrepareExecution();
            return dbUtils.Execute(callback);
        }
        public int ExecuteNonQuery()
        {
            return bulkChunkSize > 0 ? ExecuteChunks() : PrepareAndExecute();
        }
        public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bulkChunkSize > 0) return await ExecuteChunksAsync(cancellationToken).ConfigureAwait(false);
            PrepareExecution();
            return await dbUtils.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        private int PrepareAndExecute()
        {
            PrepareExecution();
            return dbUtils.ExecuteNonQuery();
        }
        private void BeginChunkedExecution()
        {
            if (bulkCommitEachChunk && dbUtils.HasTransaction)
                throw new InvalidOperationException("commitEachChunk cannot be combined with a configured transaction. Complete or remove the transaction first.");
            if (Volatile.Read(ref executionStarted) != 0)
                throw new InvalidOperationException("A write builder can only be executed once. Create a new builder for another write.");
            if (Interlocked.Exchange(ref executionStarted, 1) != 0)
                throw new InvalidOperationException("A write builder can only be executed once. Create a new builder for another write.");
        }
        private int ExecuteChunks()
        {
            BeginChunkedExecution();
            bool manageTransaction = !bulkCommitEachChunk && !dbUtils.HasTransaction;
            if (manageTransaction) dbUtils.Transaction();
            int total = 0;
            try
            {
                foreach (var chunk in ChunkRows())
                {
                    PrepareChunkExecution(chunk);
                    total += dbUtils.ExecuteNonQuery();
                }
                if (manageTransaction) dbUtils.Commit();
                return total;
            }
            catch (Exception error)
            {
                if (manageTransaction)
                {
                    try { dbUtils.Rollback(); }
                    catch (Exception cleanup) { error.Data["NetQL.RollbackError"] = cleanup; }
                }
                throw;
            }
        }
        private async Task<int> ExecuteChunksAsync(CancellationToken cancellationToken)
        {
            BeginChunkedExecution();
            bool manageTransaction = !bulkCommitEachChunk && !dbUtils.HasTransaction;
            if (manageTransaction) dbUtils.Transaction();
            int total = 0;
            try
            {
                foreach (var chunk in ChunkRows())
                {
                    PrepareChunkExecution(chunk);
                    total += await dbUtils.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }
                if (manageTransaction) await dbUtils.CommitAsync(cancellationToken).ConfigureAwait(false);
                return total;
            }
            catch (Exception error)
            {
                if (manageTransaction)
                {
                    try { await dbUtils.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception cleanup) { error.Data["NetQL.RollbackError"] = cleanup; }
                }
                throw;
            }
        }
        private IEnumerable<List<List<Set>>> ChunkRows()
        {
            int size = EffectiveChunkSize();
            for (int i = 0; i < bulkInsertData.Count; i += size)
                yield return bulkInsertData.GetRange(i, Math.Min(size, bulkInsertData.Count - i));
        }
        private int EffectiveChunkSize()
        {
            int size = bulkChunkSize;
            if (string.Equals(dbUtils.ProviderName, "SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                int columns = bulkInsertData[0].Count;
                int parameterRows = Math.Max(1, (2100 - additionalParameters.Count) / Math.Max(1, columns));
                size = Math.Min(size, Math.Min(1000, parameterRows));
            }
            return Math.Max(1, size);
        }
        private void PrepareChunkExecution(List<List<Set>> rows)
        {
            ValidateProviderLimits(rows);
            dbUtils.Query(GenerateBulkInsertQuery(rows));
            foreach (var value in rows.SelectMany(row => row))
            {
                if (value is SetRaw raw && raw.IsRaw) continue;
                dbUtils.AddParameter(value.BindName, value.Value, value.VType);
            }
            foreach (var parameter in additionalParameters)
                dbUtils.AddParameter(parameter.BindName, parameter.Value, parameter.VType);
            assignWhereParameters(whereValues, ref dbUtils);
        }
        private void PrepareExecution()
        {
            if (Volatile.Read(ref executionStarted) != 0)
                throw new InvalidOperationException("A write builder can only be executed once. Create a new builder for another write.");
            ValidateProviderLimits();
            var sql = GenerateQuery();
            if (Interlocked.Exchange(ref executionStarted, 1) != 0)
                throw new InvalidOperationException("A write builder can only be executed once. Create a new builder for another write.");

            // Never reset this flag, even if preparation, execution or the callback fails.
            dbUtils.Query(sql);
            foreach (var value in columnValues.Concat(bulkInsertData.SelectMany(row => row)))
            {
                if (value is SetRaw raw && raw.IsRaw) continue;
                dbUtils.AddParameter(value.BindName, value.Value, value.VType);
            }
            foreach (var parameter in additionalParameters)
                dbUtils.AddParameter(parameter.BindName, parameter.Value, parameter.VType);
            assignWhereParameters(whereValues, ref dbUtils);
        }
        private void ValidateProviderLimits()
        {
            ValidateProviderLimits(bulkInsertData);
        }
        private void ValidateProviderLimits(List<List<Set>> rows)
        {
            // SQL Server rejects more than 1000 rows in a multi-row VALUES list and more than
            // 2100 parameters per command; failing here keeps the provider error actionable.
            if (rows.Count == 0 || !string.Equals(dbUtils.ProviderName, "SqlServer", StringComparison.OrdinalIgnoreCase))
                return;
            if (rows.Count > 1000)
                throw new InvalidOperationException("SQL Server accepts at most 1000 rows per INSERT statement, but this batch has "
                    + rows.Count + " rows. Split the batch into chunks of 1000 rows or fewer.");
            int parameters = rows.Sum(row => row.Count) + additionalParameters.Count;
            if (parameters > 2100)
                throw new InvalidOperationException("SQL Server accepts at most 2100 parameters per command, but this batch needs "
                    + parameters + ". Split the batch into chunks with at most 2100 / columnCount rows each.");
        }
        protected DbUtils assignWhereParameters(List<SetWhere> wheres, ref DbUtils db)
        {
            foreach (var value in wheres)
            {
                if (value is SubWhere subWhere)
                    subWhere.SubDbUtil.CopyParametersTo(db);
                else if (value is SetWhereRaw raw && raw.IsRaw)
                    continue;
                else if (value is SetWhereIn inValues)
                {
                    foreach (var item in inValues.Items)
                        db.AddParameter(item.BindName, item.Value, item.VType);
                }
                else if (NullOperator(value) == null)
                    db.AddParameter(value.BindName, value.Value, value.VType);
            }
            return db;
        }
        private string RenderValue(Set value)
        {
            if (value is SetRaw raw && raw.IsRaw)
                return value.Value?.ToString() ?? "NULL";
            return value.CustomBind != null ? value.CustomBind(bindSymbol + value.BindName) : bindSymbol + value.BindName;
        }
        public string GenerateQuery()
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new InvalidOperationException("Choose an insert, update or delete operation first.");
            if (bulkInsertData.Count != 0 && (columnValues.Count != 0 || sqlModelType != SqlModelType.INSERT))
                throw new InvalidOperationException("Bulk rows can only be used in inserts without explicit column values.");
            if (sqlModelType == SqlModelType.DELETE)
                return query + GenerateWhere();
            if (bulkInsertData.Count != 0)
                return GenerateBulkInsertQuery();
            if (columnValues.Count == 0)
                throw new InvalidOperationException("Insert and update operations require at least one column value.");
            if (sqlModelType == SqlModelType.UPDATE)
                return query + " SET " + string.Join(",", columnValues.Select(value =>
                    WrapQuot(value.Column) + "=" + RenderValue(value))) + GenerateWhere();
            return query + "(" + string.Join(",", columnValues.Select(value => WrapQuot(value.Column))) +
                ") VALUES (" + string.Join(",", columnValues.Select(RenderValue)) + ")";
        }
        private string GenerateBulkInsertQuery()
        {
            return GenerateBulkInsertQuery(bulkInsertData);
        }
        private string GenerateBulkInsertQuery(List<List<Set>> data)
        {
            var columns = "(" + string.Join(",", data[0].Select(value => WrapQuot(value.Column))) + ")";
            var rows = data.Select(row => "(" + string.Join(",", row.Select(RenderValue)) + ")");
            if (string.Equals(dbUtils.ProviderName, "Oracle", StringComparison.OrdinalIgnoreCase))
            {
                var into = query.Substring("INSERT ".Length);
                return "INSERT ALL " + string.Join(" ", rows.Select(row => into + columns + " VALUES " + row)) +
                    " SELECT 1 FROM DUAL";
            }
            return query + columns + " VALUES " + string.Join(",", rows);
        }
        private static string NullOperator(SetWhere value)
        {
            if (value.CustomBind != null || value is SetWhereRaw || value is SetWhereIn || (value.Value != null && value.Value != DBNull.Value))
                return null;
            switch (value.ValueOperator?.Trim().ToUpperInvariant())
            {
                case "=":
                case "IS":
                    return "IS";
                case "!=":
                case "<>":
                case "IS NOT":
                    return "IS NOT";
                default:
                    return null;
            }
        }
        private string GenerateWhere()
        {
            var conditions = new List<string>();
            foreach (var value in whereValues)
            {
                var valueOperator = value.ValueOperator;
                string bindingValue;
                if (value is SetWhereIn chunked && string.Equals(dbUtils.ProviderName, "Oracle", StringComparison.OrdinalIgnoreCase)
                    && chunked.Items.Count > OracleInListLimit)
                {
                    bool notIn = valueOperator != null && valueOperator.Trim().Equals("NOT IN", StringComparison.OrdinalIgnoreCase);
                    var groups = new List<string>();
                    for (int i = 0; i < chunked.Items.Count; i += OracleInListLimit)
                        groups.Add(WrapQuot(value.Column) + " " + valueOperator + " (" + string.Join(",",
                            chunked.Items.Skip(i).Take(OracleInListLimit).Select(RenderValue)) + ")");
                    conditions.Add((conditions.Count == 0 ? string.Empty : value.Operator + " ") +
                        "(" + string.Join(notIn ? " AND " : " OR ", groups) + ")");
                    continue;
                }
                if (value is SetWhereIn inValues)
                    bindingValue = "(" + string.Join(",", inValues.Items.Select(RenderValue)) + ")";
                else if (value is SubWhere sub)
                    bindingValue = "(" + (sub.Column == null ? sub.SubDbUtil.GenerateWhere(true) : sub.SubDbUtil.GenerateQuery()) + ")";
                else if (value is SetWhereRaw raw && raw.IsRaw)
                    bindingValue = value.Value?.ToString() ?? "NULL";
                else if (NullOperator(value) != null)
                {
                    valueOperator = NullOperator(value);
                    bindingValue = "NULL";
                }
                else
                    bindingValue = RenderValue(value);

                var condition = string.IsNullOrEmpty(value.Column) ? bindingValue :
                    WrapQuot(value.Column) + " " + valueOperator + " " + bindingValue;
                conditions.Add((conditions.Count == 0 ? string.Empty : value.Operator + " ") + condition);
            }
            return conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" ", conditions);
        }
    }
}
